<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { useUserStore } from '@/store';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';

const props = defineProps<{
  frpId: number;
}>();

const emit = defineEmits<{
  update: [];
}>();

const userStore = useUserStore();
const terminalWrapper = ref<HTMLElement | null>(null);
const terminalBody = ref<HTMLElement | null>(null);

let term: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;
let themeObserver: MutationObserver | null = null;
let hubConnection: HubConnection | null = null;

// 主题配置
const termThemes = {
  dark: {
    background: 'transparent',
    foreground: '#cccccc',
    cursor: 'transparent',
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
  light: {
    background: 'transparent',
    foreground: '#333333',
    cursor: 'transparent',
    selectionBackground: '#add6ff',
    black: '#000000',
    red: '#cd3131',
    green: '#00bc79',
    yellow: '#9d9d10',
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

// 初始化终端
const initTerminal = () => {
  if (!terminalBody.value || !terminalWrapper.value) return;
  if (term) {
    term.clear();
    writeWelcomeMsg();
    return;
  }

  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  term = new Terminal({
    cursorBlink: false,
    cursorStyle: 'bar',
    fontSize: 14,
    fontFamily:
      '"Maple Mono", "Maple Mono CN", "Cascadia Code", Consolas, Menlo, "PingFang SC", "Microsoft YaHei", monospace',
    lineHeight: 1.4,
    theme: isDark ? termThemes.dark : termThemes.light,
    allowTransparency: true,
    disableStdin: true,
    convertEol: true,
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalBody.value);

  const fitTerminal = () => {
    if (terminalBody.value && terminalBody.value.clientWidth > 0 && terminalBody.value.clientHeight > 0) {
      try {
        fitAddon?.fit();
        term?.scrollToBottom();
      } catch (e) {
        console.warn(e);
      }
    }
  };

  resizeObserver = new ResizeObserver(() => window.requestAnimationFrame(fitTerminal));
  resizeObserver.observe(terminalWrapper.value);

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
  term?.writeln(`\x1b[1;34m[System]\x1b[0m ID: ${props.frpId} | 状态: \x1b[32m已就绪！\x1b[0m`);
};

// SignalR
const stopSignalR = async () => {
  if (hubConnection) {
    try {
      await hubConnection.stop();
      hubConnection = null;
    } catch (e) {
      console.error(e);
    }
  }
};

const startSignalR = async () => {
  await stopSignalR();
  if (!props.frpId) return;

  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/frpLogsHub', baseUrl || window.location.origin);
  if (token) hubUrl.searchParams.append('x-user-token', token);
  console.log(hubUrl.toString());

  hubConnection = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

  hubConnection.on('ReceiveLog', (message: string) => {
    term?.writeln(colorizeLog(message));
    // 包含MSLX的信息应当检查隧道状态
    if (message.includes('[MSLX]')) {
      setTimeout(() => {
        emit('update');
      }, 2000);
    }
  });

  // 重连
  hubConnection.onreconnecting((error) => {
    term?.writeln('\x1b[1;33m[System] 检测到连接中断，正在尝试重连...\x1b[0m');
    console.warn('SignalR Reconnecting:', error);
  });

  // 重连成功
  hubConnection.onreconnected(async () => {
    term?.writeln('\x1b[1;32m[System] 网络已恢复，重新连接日志服务...\x1b[0m');
    try {
      await hubConnection?.invoke('JoinGroup', props.frpId);
      term?.writeln('\x1b[1;32m[System] 日志服务成功重新连接！\x1b[0m');
    } catch (err: any) {
      term?.writeln(`\x1b[1;31m[Error] 重新连接日志服务失败: ${err.message}\x1b[0m`);
    }
  });

  // 连接彻底关了
  hubConnection.onclose((error) => {
    if (error) {
      term?.writeln(`\x1b[1;31m[System] 日志服务连接已断开: ${error.message}\x1b[0m`);
      term?.writeln('\x1b[1;31m[System] 请刷新页面或检查网络连接。\x1b[0m');
    }
  });

  try {
    await hubConnection.start();
    term?.writeln('\x1b[1;32m[System] 成功连接到Frpc日志服务\x1b[0m');
    await hubConnection.invoke('JoinGroup', props.frpId);
  } catch (err: any) {
    term?.writeln(`\x1b[1;31m[Error] 连接失败: ${err.message}\x1b[0m`);
  }
};

// 公开方法
const writeln = (msg: string) => term?.writeln(msg);
const clear = () => {
  term?.clear();
  writeWelcomeMsg();
};

defineExpose({ writeln, clear });

// 监听 ID 变化重连
watch(
  () => props.frpId,
  async (newVal) => {
    if (newVal) {
      initTerminal();
      await startSignalR();
    }
  },
);

onMounted(async () => {
  await nextTick();
  initTerminal();

  themeObserver = new MutationObserver(updateTerminalTheme);
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['theme-mode'] });

  if (props.frpId) await startSignalR();
});

onUnmounted(async () => {
  themeObserver?.disconnect();
  await stopSignalR();
  term?.dispose();
  resizeObserver?.disconnect();
});
</script>

<template>
  <div ref="terminalWrapper" class="terminal-wrapper">
    <div class="terminal-header">
      <div class="dots">
        <span class="dot red"></span><span class="dot yellow"></span><span class="dot green"></span>
      </div>
      <div class="tab-title">MSLX - Frp 控制台 | {{ frpId }}</div>
    </div>
    <div ref="terminalBody" class="terminal-body"></div>
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar.less';

.terminal-wrapper {
  flex: 1;
  display: flex;
  flex-direction: column;
  background-color: var(--td-bg-color-container);
  backdrop-filter: blur(10px);

  border: 1px solid var(--td-component-stroke);
  border-radius: 12px;
  overflow: hidden;
  box-shadow: var(--td-shadow-2);
  position: relative;
  width: 100%;
  height: 100%;

  .terminal-header {
    height: 38px;
    flex-shrink: 0;
    background-color: transparent;
    border-bottom: 1px solid var(--td-component-stroke);
    display: flex;
    align-items: center;
    padding: 0 16px;
    z-index: 10;

    .dots {
      display: flex;
      gap: 6px;
      margin-right: 16px;
      .dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
      }
      .red {
        background: #ff5f56;
      }
      .yellow {
        background: #ffbd2e;
      }
      .green {
        background: #27c93f;
      }
    }
    .tab-title {
      color: var(--td-text-color-placeholder);
      font-size: 12px;
      font-family: Menlo, monospace;
    }
  }

  .terminal-body {
    position: absolute;
    top: 38px;
    bottom: 50px;
    left: 0;
    right: 0;
    padding: 6px 0 6px 10px;
    z-index: 1;

    :deep(.xterm),
    :deep(.xterm-viewport),
    :deep(.xterm-screen),
    :deep(.xterm-scrollable-element) {
      background-color: transparent !important;
    }

    :deep(.xterm-viewport) {
      overflow-y: hidden !important;
    }

    :deep(.xterm-scrollable-element) {
      overflow-y: auto !important;
      .scrollbar-mixin();

      &::-webkit-scrollbar {
        width: 12px !important;
        background-color: transparent;
      }
      &::-webkit-scrollbar-thumb {
        background-clip: content-box;
        border: 3px solid transparent;
        border-radius: 10px;
      }
      &::-webkit-scrollbar-thumb:hover {
        border-width: 2px;
      }
    }
  }
}
</style>
