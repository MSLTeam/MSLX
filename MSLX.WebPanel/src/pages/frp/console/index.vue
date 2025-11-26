<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick, watch } from 'vue'; // 引入 watch
import { useRoute } from 'vue-router';
import { useUserStore } from '@/store';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';

import {
  PlayCircleIcon,
  StopCircleIcon,
  RefreshIcon,
  ServerIcon,
  InternetIcon,
  TimeIcon,
  CodeIcon
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { postFrpAction } from '@/api/frp';

const route = useRoute();
const userStore = useUserStore();

// --- 改动1：frpId 改为 ref，以便在 UI 中响应变化 ---
const frpId = ref(parseInt(route.params.id as string) || 0);

const isRunning = ref(false);
const loading = ref(false);

const terminalWrapper = ref<HTMLElement | null>(null);
const terminalBody = ref<HTMLElement | null>(null);
let term: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;

// ... (日志染色 colorizeLog 函数保持不变，此处省略以节省篇幅) ...
c.enabled = true;
const colorizeLog = (log: string): string => {
  if (!log) return '';
  log = log.replace(/^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}(\.\d{3})?)/, (match) => c.gray(match));
  log = log.replace(/\[I\]/g, c.green('[I]'));
  log = log.replace(/\[W\]/g, c.yellow('[W]'));
  log = log.replace(/\[E\]/g, c.red('[E]'));
  log = log.replace(/(success)/gi, (match) => c.bold.green(match));
  log = log.replace(/(start|starting)/gi, (match) => c.blue(match));
  log = log.replace(/(failed|error)/gi, (match) => c.bgRed.white(` ${match} `));
  log = log.replace(/(:\d{4,5})/g, (match) => c.cyan(match));
  log = log.replace(/(\[[0-9a-f]{8,}\])/g, (match) => c.magenta(match));
  return log;
};

const initTerminal = () => {
  if (!terminalBody.value || !terminalWrapper.value) return;

  // 如果终端已存在，不需要重新 new，只需要清屏即可
  if (term) {
    term.clear();
    writeWelcomeMsg();
    return;
  }

  term = new Terminal({
    cursorBlink: true,
    fontSize: 13,
    fontFamily: 'Menlo, Monaco, "Courier New", monospace',
    lineHeight: 1.4,
    theme: {
      background: '#1e1e1e',
      foreground: '#cccccc',
      cursor: '#ffffff',
      selectionBackground: '#264f78',
      black: '#000000',
      red: '#cd3131',
      green: '#0dbc79',
      yellow: '#e5e510',
      blue: '#2472c8',
      magenta: '#bc3fbc',
      cyan: '#11a8cd',
      white: '#e5e5e5',
      brightBlack: '#666666',
      brightRed: '#f14c4c',
      brightGreen: '#23d18b',
      brightYellow: '#f5f543',
      brightBlue: '#3b8eea',
      brightMagenta: '#d670d6',
      brightCyan: '#29b8db',
      brightWhite: '#e5e5e5',
    },
    disableStdin: true,
    convertEol: true,
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalBody.value);

  const fitTerminal = () => {
    if (terminalBody.value && terminalBody.value.clientWidth > 0 && terminalBody.value.clientHeight > 0) {
      try { fitAddon?.fit(); } catch (e) { console.warn(e); }
    }
  };

  resizeObserver = new ResizeObserver(() => {
    window.requestAnimationFrame(() => fitTerminal());
  });
  resizeObserver.observe(terminalWrapper.value);

  fitTerminal();
  setTimeout(fitTerminal, 100);

  writeWelcomeMsg();
};

const writeWelcomeMsg = () => {
  term?.writeln('\x1b[1;34m[System]\x1b[0m 正在初始化Frp控制台 ...');
  term?.writeln(`\x1b[1;34m[System]\x1b[0m ID: ${frpId.value} | 状态: \x1b[32m已就绪！\x1b[0m`);
};

// SignalR 连接
let hubConnection: HubConnection | null = null;

// stop
const stopSignalR = async () => {
  if (hubConnection) {
    try {
      await hubConnection.stop();
      hubConnection = null;
    } catch (e) {
      console.error('停止连接失败', e);
    }
  }
};

const startSignalR = async () => {
  // 先停止旧连接
  await stopSignalR();

  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/frpLogsHub', baseUrl || window.location.origin);
  if (token) hubUrl.searchParams.append('x-api-key', token);

  hubConnection = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect()
    .build();

  hubConnection.on('ReceiveLog', (message: string) => {
    term?.writeln(colorizeLog(message));
  });

  try {
    await hubConnection.start();
    term?.writeln('\x1b[1;32m[System] 成功连接到Frpc日志服务\x1b[0m');
    await hubConnection.invoke('JoinGroup', frpId.value);
  } catch (err: any) {
    term?.writeln(`\x1b[1;31m[Error] 连接失败: ${err.message}\x1b[0m`);
  }
};

const handleStart = async () => {
  loading.value = true;
  try{
    term?.writeln('\x1b[1;32m[System] 正在启动Frp服务\x1b[0m');
    await postFrpAction( 'start',frpId.value);
    isRunning.value = true;
    loading.value = false;
    MessagePlugin.success('正在启动Frp服务···');
  }catch ( e){
    term?.writeln(`\x1b[1;31m[Error] Frpc启动失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

const handleStop = async () => {
  loading.value = true;
  try{
    term?.writeln('\x1b[1;32m[System] 正在停止Frp服务\x1b[0m');
    await postFrpAction( 'stop',frpId.value);
    isRunning.value = false;
    loading.value = false;
    MessagePlugin.warning('已停止');
  }catch ( e){
    term?.writeln(`\x1b[1;31m[Error] Frpc停止失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

// 监听id
watch(
  () => route.params.id,
  async (newId) => {
    if (newId) {
      frpId.value = parseInt(newId as string);

      initTerminal();

      // 重启链接
      term?.writeln('\x1b[33m[System] 检测到 Frp ID 变更，正在切换频道...\x1b[0m');
      await startSignalR();
    }
  }
);

onMounted(async () => {
  await nextTick();
  initTerminal();
  if (frpId.value) await startSignalR();
});

onUnmounted(async () => {
  await stopSignalR();
  term?.dispose();
  resizeObserver?.disconnect();
});
</script>

<template>
  <div class="console-page">
    <div class="layout-container">

      <div class="main-terminal-area">
        <div ref="terminalWrapper" class="terminal-wrapper">
          <div class="terminal-header">
            <div class="dots">
              <span class="dot red"></span><span class="dot yellow"></span><span class="dot green"></span>
            </div>
            <div class="tab-title">MSLX - Frp 控制台 | {{ frpId }}</div>
          </div>
          <div ref="terminalBody" class="terminal-body"></div>
        </div>
      </div>

      <div class="sidebar-area">
        <div class="sidebar-content">
          <t-card class="control-card" :bordered="false">
            <div class="control-header">
              <div class="status-indicator" :class="{ running: isRunning }">
                <span class="pulse"></span>{{ isRunning ? 'Running' : 'Stopped' }}
              </div>
              <t-tag :theme="isRunning ? 'success' : 'default'" variant="light" shape="round">
                {{ isRunning ? '正常运行' : '未运行' }}
              </t-tag>
            </div>
            <div class="control-actions">
              <t-button v-if="!isRunning" theme="success" size="large" block :loading="loading" @click="handleStart">
                <template #icon><play-circle-icon /></template>启动服务
              </t-button>
              <t-button v-else theme="danger" size="large" block :loading="loading" @click="handleStop">
                <template #icon><stop-circle-icon /></template>停止服务
              </t-button>
              <t-button style="margin: 0;" variant="dashed" block @click="term?.clear()">
                <template #icon><refresh-icon /></template>清空日志
              </t-button>
            </div>
          </t-card>

          <t-card title="隧道概览" class="info-card" :bordered="false">
            <template #actions><t-button shape="circle" variant="text"><code-icon /></t-button></template>
            <div class="info-list">
              <div class="info-item"><div class="label"><server-icon /> 实例 ID</div><div class="value">#{{ frpId }}</div></div>
              <div class="info-item"><div class="label"><internet-icon /> 协议类型</div><div class="value highlight">TCP</div></div>
              <div class="info-item"><div class="label"><internet-icon /> 远程端口</div><div class="value">7000</div></div>
              <div class="info-item"><div class="label"><time-icon /> 运行时长</div><div class="value">00:24:12</div></div>
            </div>
          </t-card>
        </div>
      </div>

    </div>
  </div>
</template>

<style scoped lang="less">
.console-page {
  padding: 12px;
  background-color: var(--td-bg-color-page);
  height: calc(100vh - 64px);
  box-sizing: border-box;
  overflow: hidden;
}

.layout-container {
  display: flex;
  width: 100%;
  height: 100%;
  gap: 20px;
  align-items: stretch;
}

.main-terminal-area {
  flex: 1;
  min-width: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.sidebar-area {
  width: 320px;
  flex-shrink: 0;
  height: 100%;
  overflow-y: auto;
}

.terminal-wrapper {
  flex: 1;
  display: flex;
  flex-direction: column;
  background-color: #1e1e1e;
  border-radius: 12px;
  overflow: hidden;
  box-shadow: var(--td-shadow-2);
  position: relative;
  width: 100%;

  .terminal-header {
    height: 38px;
    flex-shrink: 0;
    background-color: #252526;
    display: flex;
    align-items: center;
    padding: 0 16px;
    border-bottom: 1px solid #1e1e1e;
    z-index: 10;

    .dots { display: flex; gap: 6px; margin-right: 16px;
      .dot { width: 10px; height: 10px; border-radius: 50%; }
      .red { background: #ff5f56; } .yellow { background: #ffbd2e; } .green { background: #27c93f; }
    }
    .tab-title { color: #999; font-size: 12px; font-family: Menlo, monospace; }
  }

  .terminal-body {
    position: absolute;
    top: 38px;
    bottom: 0;
    left: 0;
    right: 0;
    padding: 6px 0 0 10px;
    z-index: 1;

    :deep(.xterm-screen) {
      width: 100% !important;
      height: 100% !important;
    }
  }
}

.sidebar-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.control-card {
  border-radius: 12px;
  box-shadow: var(--td-shadow-1);
  background: var(--td-bg-color-container);

  .control-header {
    display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px;
    .status-indicator { display: flex; align-items: center; gap: 10px; font-weight: 600; color: var(--td-text-color-secondary);
      .pulse { width: 8px; height: 8px; border-radius: 50%; background: var(--td-bg-color-component-disabled); transition: all 0.3s; }
      &.running { color: var(--td-success-color); .pulse { background: var(--td-success-color); box-shadow: 0 0 8px var(--td-success-color); } }
    }
  }
  .control-actions {
    display: flex; flex-direction: column; width: 100%; gap: 16px;
  }
}

.info-card {
  border-radius: 12px; box-shadow: var(--td-shadow-1); background: var(--td-bg-color-container);
  .info-list { display: flex; flex-direction: column; gap: 16px;
    .info-item { display: flex; justify-content: space-between; align-items: center; padding-bottom: 12px; border-bottom: 1px dashed var(--td-component-stroke);
      &:last-child { border-bottom: none; padding-bottom: 0; }
      .label { display: flex; align-items: center; gap: 8px; color: var(--td-text-color-placeholder); font-size: 13px; }
      .value { font-family: var(--td-font-family-number); font-weight: 500; color: var(--td-text-color-primary);
        &.highlight { color: var(--td-brand-color); background: var(--td-brand-color-light); padding: 2px 8px; border-radius: 4px; font-size: 12px; }
      }
    }
  }
}

@media (max-width: 768px) {
  .console-page { height: auto; overflow-y: auto; padding: 16px; }

  .layout-container {
    flex-direction: column;
    height: auto;
  }

  .sidebar-area {
    width: 100%;
    height: auto;
  }

  .terminal-wrapper {
    min-height: 400px;
    margin-bottom: 16px;

    .terminal-body {
      position: relative;
      top: 0;
      height: 400px;
    }
  }
}
</style>
