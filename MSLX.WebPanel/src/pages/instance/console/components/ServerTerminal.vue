<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { useUserStore } from '@/store';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';
import colorizeServerLog from '@/utils/colorizeLog';

const props = defineProps<{
  serverId: number;
}>();

defineEmits<{
  update: []
}>();

const userStore = useUserStore();
const terminalWrapper = ref<HTMLElement | null>(null);
const terminalBody = ref<HTMLElement | null>(null);

let term: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;
let themeObserver: MutationObserver | null = null;
let hubConnection: HubConnection | null = null;

// 命令输入缓冲
let commandBuffer = '';

// 主题配置
const termThemes = {
  dark: {
    background: '#181818', foreground: '#cccccc', cursor: '#cccccc', selectionBackground: '#264f78',
    black: '#000000', red: '#cd3131', green: '#0dbc79', yellow: '#e5e510',
    blue: '#2472c8', magenta: '#bc3fbc', cyan: '#11a8cd', white: '#e5e5e5',
    brightBlack: '#666666', brightRed: '#f14c4c', brightGreen: '#23d18b',
    brightYellow: '#f5f543', brightBlue: '#3b8eea', brightMagenta: '#d670d6',
    brightCyan: '#29b8db', brightWhite: '#e5e5e5',
  },
  light: {
    background: '#ffffff', foreground: '#333333', cursor: '#333333', selectionBackground: '#add6ff',
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
  return colorizeServerLog(log);
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
    cursorBlink: true, // 服务器控制台通常开启光标闪烁
    cursorStyle: 'block',
    fontSize: 13,
    fontFamily: 'Menlo, Monaco, "Courier New", monospace',
    lineHeight: 1.4,
    theme: isDark ? termThemes.dark : termThemes.light,
    disableStdin: false, // 开启输入
    convertEol: true,
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalBody.value);

  // 监听输入
  term.onData((data) => {
    handleTerminalInput(data);
  });

  const fitTerminal = () => {
    if (terminalBody.value && terminalBody.value.clientWidth > 0 && terminalBody.value.clientHeight > 0) {
      try { fitAddon?.fit(); } catch (e) { console.warn(e); }
    }
  };

  resizeObserver = new ResizeObserver(() => window.requestAnimationFrame(fitTerminal));
  resizeObserver.observe(terminalWrapper.value);

  setTimeout(fitTerminal, 100);
  writeWelcomeMsg();
};

// 简单的终端输入处理（本地回显 + 缓冲）
const handleTerminalInput = async (data: string) => {
  if (!term || !props.serverId) return;

  // 回车键
  if (data === '\r') {
    term.write('\r\n'); // 换行
    if (commandBuffer.trim()) {
      await sendCommandToServer(commandBuffer);
    }
    commandBuffer = '';
  }
  // 退格键
  else if (data === '\u007F') {
    if (commandBuffer.length > 0) {
      commandBuffer = commandBuffer.slice(0, -1);
      term.write('\b \b'); // 移动光标回退，打印空格覆盖，再回退
    }
  }
  // 其他可打印字符
  else if (data >= String.fromCharCode(0x20)) {
    commandBuffer += data;
    term.write(data); // 本地回显
  }
};

const sendCommandToServer = async (cmd: string) => {
  if (!hubConnection) {
    term?.writeln('\x1b[1;31m[System] 未连接到控制服务，无法发送指令。\x1b[0m');
    return;
  }
  try {
    // 调用 SendCommand
    await hubConnection.invoke('SendCommand', props.serverId, cmd);
  } catch (err: any) {
    term?.writeln(`\x1b[1;31m[Error] 指令发送失败: ${err.message}\x1b[0m`);
  }
};

const updateTerminalTheme = () => {
  if (!term) return;
  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  term.options.theme = isDark ? termThemes.dark : termThemes.light;
};

const writeWelcomeMsg = () => {
  term?.writeln('\x1b[1;34m[System]\x1b[0m 正在连接服务器控制台 ...');
  term?.writeln(`\x1b[1;34m[System]\x1b[0m 实例 ID: ${props.serverId}`);
  term?.writeln('\x1b[90mTip: 直接输入指令并回车即可发送到服务器。\x1b[0m');
  term?.writeln('');
};

// SignalR
const stopSignalR = async () => {
  if (hubConnection) {
    try {
      // 离开组
      if (hubConnection.state === 'Connected') {
        await hubConnection.invoke('LeaveGroup', props.serverId);
      }
      await hubConnection.stop();
      hubConnection = null;
    } catch (e) { console.error(e); }
  }
};

const startSignalR = async () => {
  await stopSignalR();
  if (!props.serverId) return;

  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/instanceControlHub', baseUrl || window.location.origin);
  if (token) hubUrl.searchParams.append('x-api-key', token);

  hubConnection = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

  // 监听日志接收
  hubConnection.on('ReceiveLog', (message: string) => {
    term?.writeln(colorizeLog(message));
  });

  // 监听指令结果 (不显示在终端，或者只显示错误)
  hubConnection.on('CommandResult', (success: boolean, msg: string) => {
    if (!success) {
      term?.writeln(`\x1b[1;31m[System] 指令执行反馈: ${msg}\x1b[0m`);
    }
  });

  hubConnection.onreconnecting(() => {
    term?.writeln('\x1b[1;31m[System] 连接中断，尝试重连...\x1b[0m');
  });

  hubConnection.onreconnected(async () => {
    term?.writeln('\x1b[1;32m[System] 网络恢复，重新订阅日志...\x1b[0m');
    try {
      await hubConnection?.invoke('JoinGroup', props.serverId);
    } catch (err: any) {
      term?.writeln(`\x1b[1;31m[Error] 重新订阅失败: ${err.message}\x1b[0m`);
    }
  });

  hubConnection.onclose((error) => {
    if (error) {
      term?.writeln(`\x1b[1;31m[System] 连接已断开: ${error.message}\x1b[0m`);
    }
  });

  try {
    await hubConnection.start();
    term?.writeln('\x1b[1;32m[System] 已连接到实例控制服务\x1b[0m');
    // 加入组
    await hubConnection.invoke('JoinGroup', props.serverId);
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

watch(() => props.serverId, async (newVal) => {
  if (newVal) {
    initTerminal();
    await startSignalR();
  }
});

onMounted(async () => {
  await nextTick();
  initTerminal();

  themeObserver = new MutationObserver(updateTerminalTheme);
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['theme-mode'] });

  if (props.serverId) await startSignalR();
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
      <div class="tab-title">Server Console | Instance #{{ serverId }}</div>
    </div>
    <div ref="terminalBody" class="terminal-body"></div>
  </div>
</template>

<style scoped lang="less">
.terminal-wrapper {
  flex: 1;
  display: flex;
  flex-direction: column;
  background-color: var(--td-bg-color-container);
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
    background-color: var(--td-border-level-1-color);
    border-bottom: 1px solid var(--td-component-stroke);
    display: flex;
    align-items: center;
    padding: 0 16px;
    z-index: 10;
    user-select: none;

    .dots {
      display: flex; gap: 6px; margin-right: 16px;
      .dot { width: 10px; height: 10px; border-radius: 50%; }
      .red { background: #ff5f56; }
      .yellow { background: #ffbd2e; }
      .green { background: #27c93f; }
    }
    .tab-title {
      color: var(--td-text-color-placeholder);
      font-size: 12px;
      font-family: Menlo, monospace;
    }
  }

  .terminal-body {
    position: absolute;
    top: 38px; bottom: 0; left: 0; right: 0;
    padding: 6px 0 6px 10px; // 底部留白略微增加
    z-index: 1;

    :deep(.xterm-screen) {
      width: 100% !important; height: 100% !important;
    }

    /* 滚动条美化 */
    :deep(.xterm-viewport) {
      scrollbar-width: thin;
      scrollbar-color: rgba(121, 121, 121, 0.4) transparent;
      &::-webkit-scrollbar { width: 10px; height: 10px; }
      &::-webkit-scrollbar-track { background: transparent; }
      &::-webkit-scrollbar-thumb {
        background-color: rgba(121, 121, 121, 0.4);
        border-radius: 6px;
        border: 2px solid transparent;
        background-clip: content-box;
        &:hover { background-color: rgba(121, 121, 121, 0.7); }
      }
      &::-webkit-scrollbar-corner { background: transparent; }
    }
  }
}
</style>
