<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';
import { useInstanceHubStore } from '@/store/modules/instanceHub'; // 引入 Store
import colorizeServerLog from '@/utils/colorizeLog';

const props = defineProps<{
  serverId: number;
}>();

const emits = defineEmits<{
  update: [];
}>();

// 使用 Store
const hubStore = useInstanceHubStore();

const terminalWrapper = ref<HTMLElement | null>(null);
const terminalBody = ref<HTMLElement | null>(null);

let term: Terminal | null = null;
let fitAddon: FitAddon | null = null;
let resizeObserver: ResizeObserver | null = null;
let themeObserver: MutationObserver | null = null;

// 取消订阅函数的引用
let cleanupLog: (() => void) | null = null;
let cleanupCmdResult: (() => void) | null = null;

// 命令输入缓冲
let commandBuffer = '';
const inputCommand = ref('');

// 主题配置
const termThemes = {
  dark: {
    background: 'transparent',
    foreground: '#cccccc',
    cursor: '#cccccc',
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
    cursor: '#333333',
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
    brightYellow: '#f3d61a',
    brightBlue: '#3b8eea',
    brightMagenta: '#d670d6',
    brightCyan: '#29b8db',
    brightWhite: '#e5e5e5',
  },
};

// 日志染色
c.enabled = true;
const colorizeLog = (log: string): string => colorizeServerLog(log);

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
    convertEol: true,
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalBody.value);

  term.onData((data) => handleTerminalInput(data));

  const fitTerminal = () => {
    if (terminalBody.value && terminalBody.value.clientWidth > 0 && terminalBody.value.clientHeight > 0) {
      try {
        fitAddon?.fit();
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

// 终端输入处理
const handleTerminalInput = async (data: string) => {
  if (!term || !props.serverId) return;
  if (data === '\r') {
    term.write('\r\n');
    if (commandBuffer.trim()) {
      await sendCommandToServer(commandBuffer);
    }
    commandBuffer = '';
  } else if (data === '\u007F') {
    if (commandBuffer.length > 0) {
      commandBuffer = commandBuffer.slice(0, -1);
      term.write('\b \b');
    }
  } else if (data >= String.fromCharCode(0x20)) {
    commandBuffer += data;
    term.write(data);
  }
};

const handleSendInput = async () => {
  if (!inputCommand.value) return;
  const cmd = inputCommand.value;
  term?.writeln(cmd);
  await sendCommandToServer(cmd);
  inputCommand.value = '';
};

// 使用 Store 发送指令
const sendCommandToServer = async (cmd: string) => {
  try {
    await hubStore.sendCommand(cmd);
  } catch (err: any) {
    term?.writeln(`\x1b[1;31m[Error] ${err.message}\x1b[0m`);
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
  term?.writeln('');
};

// 连接 Store
const connectStore = async () => {
  if (!props.serverId) return;

  // 订阅日志
  if (cleanupLog) cleanupLog();
  cleanupLog = hubStore.onLog((msg) => {
    // 确保 term 存在再写入
    if (term) {
      term.writeln(colorizeLog(msg));
    }
    if (msg.startsWith('[MSLX]')) {
      emits('update');
    }
  });

  // 订阅指令结果
  if (cleanupCmdResult) cleanupCmdResult();
  cleanupCmdResult = hubStore.onCommandResult((success, msg) => {
    if (!success) {
      term?.writeln(`\x1b[1;31m[System] 指令执行反馈: ${msg}\x1b[0m`);
    }
  });

  // 发起连接
  await hubStore.connect(props.serverId);
};

const disconnectStore = async () => {
  // 取消回调订阅
  if (cleanupLog) cleanupLog();
  if (cleanupCmdResult) cleanupCmdResult();

  // 告知 Store 本组件退出
  await hubStore.disconnect();
};

const writeln = (msg: string) => term?.writeln(msg);
const clear = () => {
  term?.clear();
  writeWelcomeMsg();
};
defineExpose({ writeln, clear });

// 监听 ServerId 变化
watch(
  () => props.serverId,
  async (newVal, oldVal) => {
    if (newVal !== oldVal) {
      await disconnectStore(); // 断开旧的引用
      initTerminal();
      await connectStore(); // 建立新的引用
    }
  },
);

onMounted(async () => {
  await nextTick();
  initTerminal();

  themeObserver = new MutationObserver(updateTerminalTheme);
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['theme-mode'] });

  await connectStore();
});

onUnmounted(async () => {
  themeObserver?.disconnect();
  resizeObserver?.disconnect();
  term?.dispose();
  await disconnectStore();
});
</script>

<template>
  <div ref="terminalWrapper" class="terminal-wrapper flex-1 flex flex-col bg-[var(--td-bg-color-container)]/80 border border-[var(--td-component-border)] rounded-xl overflow-hidden shadow-sm relative w-full h-full">

    <div class="h-[38px] shrink-0 bg-transparent border-b border-[var(--td-component-border)] flex items-center px-4 relative z-10 select-none">
      <div class="flex gap-1.5 mr-4">
        <span class="w-2.5 h-2.5 rounded-full bg-[#ff5f56]"></span>
        <span class="w-2.5 h-2.5 rounded-full bg-[#ffbd2e]"></span>
        <span class="w-2.5 h-2.5 rounded-full bg-[#27c93f]"></span>
      </div>
      <div class="text-[var(--td-text-color-secondary)] text-xs font-mono truncate">
        MSLX 服务端控制台 | #{{ serverId }}
      </div>
    </div>

    <div ref="terminalBody" class="absolute top-[38px] bottom-[50px] left-0 right-0 py-1.5 pl-2.5 z-[1] terminal-body-container"></div>

    <div class="absolute bottom-0 left-0 right-0 h-[50px] flex items-center px-4 bg-transparent border-t border-[var(--td-component-border)] z-10 gap-3">
      <input
        v-model="inputCommand"
        class="flex-1 h-8 bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200 dark:border-zinc-700 rounded-md px-3 text-[var(--td-text-color-primary)] font-mono text-[13px] outline-none transition-all focus:border-[var(--color-primary)] focus:bg-white dark:focus:bg-zinc-900 placeholder:text-zinc-400 dark:placeholder:text-zinc-500"
        placeholder="发送控制台指令..."
        @keyup.enter="handleSendInput"
      />
      <button
        class="h-8 px-4 rounded-md bg-[var(--color-primary)] text-white text-[13px] font-medium transition-all hover:brightness-110 active:brightness-90"
        @click="handleSendInput"
      >
        发送
      </button>
    </div>
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar.less';

.terminal-body-container {
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
      background-color: #d4d4d8; /* zinc-300 */
    }
    :global(html[theme-mode='dark']) &::-webkit-scrollbar-thumb,
    :global(html.dark) &::-webkit-scrollbar-thumb {
      background-color: #52525b; /* zinc-600 */
    }
    &::-webkit-scrollbar-thumb:hover {
      border-width: 2px;
    }
  }
}
</style>
