<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { useUserStore } from '@/store';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

// 终端相关
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';

// 图标与组件
import {
  CloudIcon,
  CodeIcon,
  InternetIcon,
  LinkIcon,
  PlayCircleIcon,
  RefreshIcon,
  ServerIcon,
  StopCircleIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

// API
import { getTunnelInfo, postFrpAction } from '@/api/frp';
import { TunnelInfoModel } from '@/api/model/frp';

const route = useRoute();
const userStore = useUserStore();

// 状态
const frpId = ref(parseInt(route.params.id as string) || 0);
const isRunning = ref(false);
const loading = ref(false);
const tunnelInfo = ref<TunnelInfoModel | null>(null);

// 终端 DOM
const terminalWrapper = ref<HTMLElement | null>(null);
const terminalBody = ref<HTMLElement | null>(null);
let term: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;
let themeObserver: MutationObserver | null = null; // 主题监听器

// 两套主图
const termThemes = {
  dark: {
    background: '#181818',
    foreground: '#cccccc',
    cursor: '#ffffff',
    selectionBackground: '#264f78',
    black: '#000000', red: '#cd3131', green: '#0dbc79', yellow: '#e5e510',
    blue: '#2472c8', magenta: '#bc3fbc', cyan: '#11a8cd', white: '#e5e5e5',
    brightBlack: '#666666', brightRed: '#f14c4c', brightGreen: '#23d18b',
    brightYellow: '#f5f543', brightBlue: '#3b8eea', brightMagenta: '#d670d6',
    brightCyan: '#29b8db', brightWhite: '#e5e5e5',
  },
  light: {
    background: '#ffffff',
    foreground: '#333333',
    cursor: '#333333',
    selectionBackground: '#add6ff',
    black: '#000000', red: '#cd3131', green: '#00bc79', yellow: '#9d9d10',
    blue: '#2472c8', magenta: '#bc3fbc', cyan: '#11a8cd', white: '#e5e5e5',
    brightBlack: '#666666', brightRed: '#f14c4c', brightGreen: '#23d18b',
    brightYellow: '#f5f543', brightBlue: '#3b8eea', brightMagenta: '#d670d6',
    brightCyan: '#29b8db', brightWhite: '#e5e5e5',
  }
};

// 日志染色
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

// 初始化 + 主题监听
const initTerminal = () => {
  if (!terminalBody.value || !terminalWrapper.value) return;

  if (term) {
    term.clear();
    writeWelcomeMsg();
    return;
  }

  // 检测初始主题
  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';

  term = new Terminal({
    cursorBlink: true,
    cursorStyle: 'bar',
    fontSize: 13,
    fontFamily: 'Menlo, Monaco, "Courier New", monospace',
    lineHeight: 1.4,
    theme: isDark ? termThemes.dark : termThemes.light, // 动态设置
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

const updateTerminalTheme = () => {
  if (!term) return;
  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  term.options.theme = isDark ? termThemes.dark : termThemes.light;
};

const writeWelcomeMsg = () => {
  term?.writeln('\x1b[1;34m[System]\x1b[0m 正在初始化Frp控制台 ...');
  term?.writeln(`\x1b[1;34m[System]\x1b[0m ID: ${frpId.value} | 状态: \x1b[32m已就绪！\x1b[0m`);
};

// 获取隧道详情
async function getThisTunnelInfo() {
  try {
    tunnelInfo.value = await getTunnelInfo(frpId.value);
    isRunning.value = tunnelInfo.value.isRunning;
  } catch (error) {
    console.error('获取隧道信息失败', error);
    term?.writeln(`\x1b[1;31m[Error] 获取隧道信息失败: ${error}\x1b[0m`);
  }
}

// SignalR 连接
let hubConnection: HubConnection | null = null;
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
  try {
    term?.writeln('\x1b[1;32m[System] 正在发送启动指令...\x1b[0m');
    await postFrpAction('start', frpId.value);
    isRunning.value = true;
    loading.value = false;
    MessagePlugin.success('启动指令已发送');
    setTimeout(getThisTunnelInfo, 1500);
  } catch (e: any) {
    term?.writeln(`\x1b[1;31m[Error] Frpc启动失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

const handleStop = async () => {
  loading.value = true;
  try {
    term?.writeln('\x1b[1;32m[System] 正在发送停止指令...\x1b[0m');
    await postFrpAction('stop', frpId.value);
    isRunning.value = false;
    loading.value = false;
    MessagePlugin.warning('停止指令已发送');
    setTimeout(getThisTunnelInfo, 1000);
  } catch (e: any) {
    term?.writeln(`\x1b[1;31m[Error] Frpc停止失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

watch(
  () => route.params.id,
  async (newId) => {
    if (newId) {
      frpId.value = parseInt(newId as string);
      initTerminal();
      term?.writeln('\x1b[33m[System] 检测到 Frp ID 变更，正在刷新数据...\x1b[0m');
      await getThisTunnelInfo();
      await startSignalR();
    }
  },
);

onMounted(async () => {
  await nextTick();
  initTerminal();

  // 监听 主题切换
  themeObserver = new MutationObserver(() => updateTerminalTheme());
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['theme-mode'], // 仅监听 theme-mode 属性变化
  });

  if (frpId.value) {
    Promise.all([getThisTunnelInfo(), startSignalR()]);
  }
});

onUnmounted(async () => {
  themeObserver?.disconnect(); // 销毁主题监听
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
              <t-button v-if="!isRunning" theme="primary" size="large" block :loading="loading" @click="handleStart">
                <template #icon><play-circle-icon /></template>启动服务
              </t-button>
              <t-button v-else theme="danger" size="large" block :loading="loading" @click="handleStop">
                <template #icon><stop-circle-icon /></template>停止服务
              </t-button>
              <t-button style="margin: 0" variant="dashed" block @click="term?.clear()">
                <template #icon><refresh-icon /></template>清空日志
              </t-button>
            </div>
          </t-card>

          <t-card title="隧道概览" class="info-card" :bordered="false">
            <template #actions><t-button shape="circle" variant="text"><code-icon /></t-button></template>

            <div class="info-list">
              <div class="info-item">
                <div class="label"><server-icon /> 隧道实例 ID</div>
                <div class="value">#{{ frpId }}</div>
              </div>

              <template v-if="tunnelInfo && tunnelInfo.proxies && tunnelInfo.proxies.length > 0">
                <div v-for="(proxy, index) in tunnelInfo.proxies" :key="index" class="proxy-group">
                  <div v-if="tunnelInfo.proxies.length > 1" class="proxy-header">配置 #{{ index + 1 }}</div>

                  <div class="info-item">
                    <div class="label"><cloud-icon /> 隧道名称</div>
                    <div class="value">{{ proxy.proxyName }}</div>
                  </div>

                  <div class="info-item">
                    <div class="label"><internet-icon /> 协议类型</div>
                    <div class="value highlight">{{ proxy.type.toUpperCase() }}</div>
                  </div>

                  <div class="info-item">
                    <div class="label"><link-icon /> 连接地址</div>
                    <t-tooltip :content="proxy.remoteAddressMain" placement="top" show-arrow destroy-on-close>
                      <div class="value remote-addr pointer">{{ proxy.remoteAddressMain || '获取中...' }}</div>
                    </t-tooltip>
                  </div>

                  <div v-if="proxy.remoteAddressBackup && proxy.remoteAddressBackup !== proxy.remoteAddressMain" class="info-item">
                    <div class="label"><link-icon /> 备用地址</div>
                    <t-tooltip :content="proxy.remoteAddressBackup" placement="top" show-arrow destroy-on-close>
                      <div class="value remote-addr pointer">{{ proxy.remoteAddressBackup }}</div>
                    </t-tooltip>
                  </div>

                  <div class="info-item">
                    <div class="label"><code-icon /> 本地地址</div>
                    <div class="value">{{ proxy.localAddress }}</div>
                  </div>
                </div>
              </template>

              <div v-else class="info-item empty-state">
                <div class="label">状态</div>
                <div class="value">{{ loading ? '正在加载配置...' : '暂无详细信息' }}</div>
              </div>
            </div>
          </t-card>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
.console-page {
  padding-bottom: 12px;
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

// 终端外壳样式 (适配深浅模式)
.terminal-wrapper {
  flex: 1;
  display: flex;
  flex-direction: column;

  // 使用 TDesign 变量，自动跟随主题变色
  background-color: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke); // 亮色模式下提供边框区分

  border-radius: 12px;
  overflow: hidden;
  box-shadow: var(--td-shadow-2);
  position: relative;
  width: 100%;

  .terminal-header {
    height: 38px;
    flex-shrink: 0;

    background-color: var(--td-border-level-1-color);
    border-bottom: 1px solid var(--td-component-stroke);

    display: flex;
    align-items: center;
    padding: 0 16px;
    z-index: 10;

    .dots {
      display: flex; gap: 6px; margin-right: 16px;
      .dot { width: 10px; height: 10px; border-radius: 50%; }
      .red { background: #ff5f56; }
      .yellow { background: #ffbd2e; }
      .green { background: #27c93f; }
    }
    .tab-title {
      // 字体颜色自动适配
      color: var(--td-text-color-placeholder);
      font-size: 12px;
      font-family: Menlo, monospace;
    }
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

// 侧边栏样式
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
    .status-indicator {
      display: flex; align-items: center; gap: 10px; font-weight: 600;
      color: var(--td-text-color-secondary);
      .pulse {
        width: 8px; height: 8px; border-radius: 50%;
        background: var(--td-bg-color-component-disabled); transition: all 0.3s;
      }
      &.running {
        color: var(--td-success-color);
        .pulse { background: var(--td-success-color); box-shadow: 0 0 8px var(--td-success-color); }
      }
    }
  }
  .control-actions {
    display: flex; flex-direction: column; width: 100%; gap: 16px;
  }
}

.info-card {
  border-radius: 12px;
  box-shadow: var(--td-shadow-1);
  background: var(--td-bg-color-container);

  .info-list {
    display: flex; flex-direction: column; gap: 12px;

    .proxy-group {
      display: flex; flex-direction: column; gap: 12px; padding-top: 12px;
      border-top: 1px dashed var(--td-component-stroke);
      &:first-child { border-top: none; padding-top: 0; }
    }
    .proxy-header { font-size: 12px; font-weight: bold; color: var(--td-text-color-secondary); }

    .info-item {
      display: flex; justify-content: space-between; align-items: center;
      .label { display: flex; align-items: center; gap: 8px; color: var(--td-text-color-placeholder); font-size: 13px; }
      .value {
        font-family: var(--td-font-family-number); font-weight: 500;
        color: var(--td-text-color-primary); font-size: 13px;
        &.highlight { color: var(--td-brand-color); background: var(--td-brand-color-light); padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        &.remote-addr {
          max-width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer;
          &:hover { color: var(--td-brand-color); }
        }
        &.pointer { cursor: pointer; }
      }
    }
    .empty-state { padding: 12px 0; color: var(--td-text-color-placeholder); }
  }
}

@media (max-width: 768px) {
  .console-page { height: auto; overflow-y: auto; padding: 16px; }
  .layout-container { flex-direction: column; height: auto; }
  .sidebar-area { width: 100%; height: auto; }
  .terminal-wrapper {
    min-height: 400px; margin-bottom: 16px;
    .terminal-body { position: relative; top: 0; height: 400px; }
  }
}
</style>
