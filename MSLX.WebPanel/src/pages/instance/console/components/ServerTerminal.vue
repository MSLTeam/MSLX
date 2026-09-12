<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import c from 'ansi-colors';
import '@xterm/xterm/css/xterm.css';
import { useInstanceHubStore } from '@/store/modules/instanceHub';
import colorizeServerLog from '@/utils/colorizeLog';

const props = defineProps<{
  serverId: number;
  enablePty?: boolean;
}>();

const emits = defineEmits<{
  update: [];
}>();

// 使用 Store
const hubStore = useInstanceHubStore();

const terminalWrapper = ref<HTMLElement | null>(null);
const logTerminalBody = ref<HTMLElement | null>(null);
const ptyTerminalBody = ref<HTMLElement | null>(null);

let logTerm: Terminal | null = null;
let logFitAddon: FitAddon | null = null;

let ptyTerm: Terminal | null = null;
let ptyFitAddon: FitAddon | null = null;

let resizeObserver: ResizeObserver | null = null;
let themeObserver: MutationObserver | null = null;

// 取消订阅函数的引用
let cleanupLog: (() => void) | null = null;
let cleanupSystemMsg: (() => void) | null = null;
let cleanupCmdResult: (() => void) | null = null;
let cleanupPtyData: (() => void) | null = null;
let cleanupPtyStatus: (() => void) | null = null;

// PTY 终端模式与状态
let userManuallySwitched = false;
const isPtyMode = ref(false);
const serverPtyStatus = ref<{ isPty: boolean; isRunning: boolean; enablePty?: boolean }>({
  isPty: false,
  isRunning: false,
  enablePty: false,
});

// 当前实例是否启用了 PTY
const isPtyConfigured = computed(() => {
  if (typeof props.enablePty === 'boolean') {
    return props.enablePty;
  }
  return !!serverPtyStatus.value.enablePty || serverPtyStatus.value.isPty;
});

// 命令输入缓冲
let commandBuffer = '';
const inputCommand = ref('');

// === 命令历史记录功能 ===
const LOCAL_STORAGE_KEY = 'mslx_console_history';
const commandHistory = ref<string[]>([]);
const historyIndex = ref(-1);

// 加载历史记录
const loadHistory = () => {
  const stored = localStorage.getItem(LOCAL_STORAGE_KEY);
  if (stored) {
    try {
      commandHistory.value = JSON.parse(stored);
      historyIndex.value = commandHistory.value.length;
    } catch (e) {
      console.error('解析指令历史记录失败', e);
    }
  }
};

// 记录到历史
const addCommandToHistory = (cmd: string) => {
  const trimmedCmd = cmd.trim();
  if (!trimmedCmd) return;
  if (commandHistory.value[commandHistory.value.length - 1] !== trimmedCmd) {
    commandHistory.value.push(trimmedCmd);
    if (commandHistory.value.length > 15) {
      commandHistory.value.shift();
    }
    localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(commandHistory.value));
  }
  historyIndex.value = commandHistory.value.length;
};

// 向上切换
const handleHistoryUp = () => {
  if (commandHistory.value.length === 0) return;
  if (historyIndex.value > 0) {
    historyIndex.value--;
    inputCommand.value = commandHistory.value[historyIndex.value];
  }
};

// 向下切换
const handleHistoryDown = () => {
  if (commandHistory.value.length === 0) return;
  if (historyIndex.value < commandHistory.value.length - 1) {
    historyIndex.value++;
    inputCommand.value = commandHistory.value[historyIndex.value];
  } else {
    historyIndex.value = commandHistory.value.length;
    inputCommand.value = '';
  }
};

// 获取当前系统品牌主题色
const getThemePrimaryColor = () => {
  if (typeof window === 'undefined') return '#0052d9';
  const rootStyle = getComputedStyle(document.documentElement);
  const color =
    rootStyle.getPropertyValue('--td-brand-color').trim() ||
    rootStyle.getPropertyValue('--color-primary').trim();
  return color || '#0052d9';
};

// 处理浅色模式下的选择背景
const getLightSelectionBg = (hexColor: string, weight = 0.28): string => {
  let color = hexColor.trim();
  if (!color.startsWith('#')) return '#bfdbfe'; // 默认优雅明亮的淡天蓝
  if (color.length === 4) {
    color = `#${color[1]}${color[1]}${color[2]}${color[2]}${color[3]}${color[3]}`;
  }
  if (color.length >= 7) {
    const r = parseInt(color.slice(1, 3), 16);
    const g = parseInt(color.slice(3, 5), 16);
    const b = parseInt(color.slice(5, 7), 16);
    if (!isNaN(r) && !isNaN(g) && !isNaN(b)) {
      const nr = Math.round(r * weight + 255 * (1 - weight));
      const ng = Math.round(g * weight + 255 * (1 - weight));
      const nb = Math.round(b * weight + 255 * (1 - weight));
      return `#${nr.toString(16).padStart(2, '0')}${ng.toString(16).padStart(2, '0')}${nb.toString(16).padStart(2, '0')}`;
    }
  }
  return '#bfdbfe';
};

// 动态生成 xterm 原生主题配置
const getTermTheme = (isDark: boolean) => {
  const primaryColor = getThemePrimaryColor();
  if (isDark) {
    return {
      background: 'transparent',
      foreground: '#f4f4f5',
      cursor: primaryColor, // 系统主题色
      cursorAccent: '#18181b',
      selectionBackground: `${primaryColor}40`,
      selectionForeground: '#ffffff',
      black: '#a1a1aa',
      red: '#f87171',
      green: '#34d399',
      yellow: '#fbbf24',
      blue: '#60a5fa',
      magenta: '#c084fc',
      cyan: '#22d3ee',
      white: '#f4f4f5',
      brightBlack: '#71717a',
      brightRed: '#fca5a5',
      brightGreen: '#6ee7b7',
      brightYellow: '#fde047',
      brightBlue: '#93c5fd',
      brightMagenta: '#d8b4fe',
      brightCyan: '#67e8f9',
      brightWhite: '#ffffff',
    };
  }

  // 浅色模式：白色/高亮白全部直接映射为黑色，光标直接使用系统品牌主题色
  return {
    background: 'transparent',
    foreground: '#18181b',
    cursor: primaryColor, // 光标跟随系统品牌主题色
    cursorAccent: '#ffffff',
    selectionBackground: getLightSelectionBg(primaryColor, 0.28), // 优雅明亮的浅色选区（不透明 hex，防止 transparent 黑底混合）
    selectionInactiveBackground: getLightSelectionBg(primaryColor, 0.16),
    selectionForeground: undefined, // 不强行覆盖前景色，完整保留文本原有的语法高亮与对比度
    black: '#18181b',
    red: '#dc2626',
    green: '#16a34a',
    yellow: '#d97706',
    blue: '#2563eb',
    magenta: '#9333ea',
    cyan: '#0284c7',
    white: '#18181b',       // 白色直接设为黑色
    brightBlack: '#71717a',
    brightRed: '#ef4444',
    brightGreen: '#22c55e',
    brightYellow: '#f59e0b',
    brightBlue: '#3b82f6',
    brightMagenta: '#a855f7',
    brightCyan: '#06b6d4',
    brightWhite: '#18181b', // 高亮白直接设为黑色
  };
};

// 日志染色
c.enabled = true;
const colorizeLog = (log: string): string => colorizeServerLog(log);

// 为 PTY 终端原始数据流应用实时日志染色规则
const colorizePtyChunk = (chunk: string): string => {
  if (!chunk) return chunk;

  // 按换行符切分，保护终端换行控制符
  const parts = chunk.split(/(\r?\n)/);
  const result: string[] = [];

  for (let i = 0; i < parts.length; i++) {
    let part = parts[i];
    if (part === '\n' || part === '\r\n') {
      result.push(part);
    } else if (part.length > 0) {
      // 提取行首可能附带的光标移动/擦除控制序列（如 \r、\x1b[2K 等），使正则能正确定位行首的时间戳
      let prefix = '';
      // eslint-disable-next-line no-control-regex
      const matchPrefix = part.match(/^(\x1b\[[0-9;]*[a-zA-Z]|\r)+/);
      if (matchPrefix) {
        prefix = matchPrefix[0];
        part = part.slice(prefix.length);
      }

      // 判断是否包含 Minecraft 服务端标准日志结构特征
      const isLogLine =
        part.includes('INFO') ||
        part.includes('WARN') ||
        part.includes('ERROR') ||
        part.includes('FATAL') ||
        part.includes('DEBUG') ||
        /^\[\d{2}:\d{2}:\d{2}/.test(part) ||
        part.includes('[MSLX') ||
        part.includes('[System]') ||
        part.startsWith('>>>');

      if (isLogLine) {
        result.push(prefix + colorizeLog(part));
      } else {
        result.push(prefix + part);
      }
    }
  }

  return result.join('');
};

const terminalFontFamily =
  '"Maple Mono", "Maple Mono CN", "Cascadia Code", Consolas, Menlo, "PingFang SC", "Microsoft YaHei", monospace';

// 初始化日志终端
const initLogTerminal = () => {
  if (!logTerminalBody.value || logTerm) return;

  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  logTerm = new Terminal({
    cursorBlink: false,
    cursorStyle: 'bar',
    fontSize: 14,
    fontFamily: terminalFontFamily,
    lineHeight: 1.4,
    theme: getTermTheme(isDark),
    allowTransparency: true,
    convertEol: true,
    smoothScrollDuration: 200,
    fastScrollSensitivity: 5,
    scrollback: 5000,
  });

  logTerm.attachCustomKeyEventHandler((arg: KeyboardEvent) => {
    if (arg.type === 'keydown' && (arg.ctrlKey || arg.metaKey) && arg.code === 'KeyC') {
      if (logTerm && logTerm.hasSelection()) {
        return false;
      }
    }
    return true;
  });

  logFitAddon = new FitAddon();
  logTerm.loadAddon(logFitAddon);
  logTerm.open(logTerminalBody.value);

  logTerm.onData((data) => {
    handleTerminalInput(data);
  });

  writeWelcomeMsg();
};

// 初始化 PTY 交互终端
const initPtyTerminal = () => {
  if (!ptyTerminalBody.value || ptyTerm) return;

  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  ptyTerm = new Terminal({
    cursorBlink: !!serverPtyStatus.value.isRunning,
    cursorStyle: 'block',
    fontSize: 14,
    fontFamily: terminalFontFamily,
    lineHeight: 1.4,
    theme: getTermTheme(isDark),
    allowTransparency: true,
    convertEol: true,
    smoothScrollDuration: 200,
    fastScrollSensitivity: 5,
    scrollback: 5000,
  });

  ptyTerm.attachCustomKeyEventHandler((arg: KeyboardEvent) => {
    if (arg.type === 'keydown' && (arg.ctrlKey || arg.metaKey) && arg.code === 'KeyC') {
      if (ptyTerm && ptyTerm.hasSelection()) {
        return false;
      }
    }
    return true;
  });

  ptyFitAddon = new FitAddon();
  ptyTerm.loadAddon(ptyFitAddon);
  ptyTerm.open(ptyTerminalBody.value);

  ptyTerm.onData((data) => {
    hubStore.sendPtyInput(data);
  });

  ptyTerm.onResize(({ cols, rows }) => {
    if (isPtyMode.value) {
      hubStore.resizePty(cols, rows);
    }
  });

  writePtyWelcomeMsg();
};

const fitTerminals = () => {
  if (logTerminalBody.value && logTerminalBody.value.clientWidth > 0 && logTerminalBody.value.clientHeight > 0) {
    try {
      logFitAddon?.fit();
    } catch (e) {
      console.warn(e);
    }
  }
  if (ptyTerminalBody.value && ptyTerminalBody.value.clientWidth > 0 && ptyTerminalBody.value.clientHeight > 0) {
    try {
      ptyFitAddon?.fit();
    } catch (e) {
      console.warn(e);
    }
  }
};

// 终端输入处理（日志模式下的回车与输入）
const handleTerminalInput = async (data: string) => {
  if (!logTerm || !props.serverId) return;
  if (data === '\r') {
    logTerm.write('\r\n');
    if (commandBuffer.trim()) {
      await sendCommandToServer(commandBuffer);
    }
    commandBuffer = '';
  } else if (data === '\u007F') {
    if (commandBuffer.length > 0) {
      commandBuffer = commandBuffer.slice(0, -1);
      logTerm.write('\b \b');
    }
  } else if (data >= String.fromCharCode(0x20)) {
    commandBuffer += data;
    logTerm.write(data);
  }
};

const handleSendInput = async () => {
  if (!inputCommand.value) return;
  const cmd = inputCommand.value;
  logTerm?.writeln(cmd);
  await sendCommandToServer(cmd);
  inputCommand.value = '';
};

// 使用 Store 发送指令
const sendCommandToServer = async (cmd: string) => {
  try {
    addCommandToHistory(cmd);
    await hubStore.sendCommand(cmd);
  } catch (err: any) {
    const errMsg = `\x1b[1;31m[Error] ${err.message}\x1b[0m`;
    logTerm?.writeln(errMsg);
    ptyTerm?.writeln(errMsg);
  }
};

const updateTerminalTheme = () => {
  const isDark = document.documentElement.getAttribute('theme-mode') === 'dark';
  const theme = getTermTheme(isDark);
  if (logTerm) logTerm.options.theme = theme;
  if (ptyTerm) ptyTerm.options.theme = theme;
};

const writeWelcomeMsg = () => {
  logTerm?.writeln('\x1b[1;34m[System]\x1b[0m 正在连接服务器控制台 ...');
  logTerm?.writeln(`\x1b[1;34m[System]\x1b[0m 实例 ID: ${props.serverId}`);
  logTerm?.writeln('');
};

const writePtyWelcomeMsg = () => {
  ptyTerm?.writeln('\x1b[1;34m[System]\x1b[0m 正在连接服务器控制台 ...');
  ptyTerm?.writeln(`\x1b[1;34m[System]\x1b[0m 实例 ID: ${props.serverId}`);
  ptyTerm?.writeln('');
};

// 模式切换
const toggleTerminalMode = async (mode: 'log' | 'pty', isManual = false) => {
  if (isManual) {
    userManuallySwitched = true;
  }
  if (isPtyMode.value === (mode === 'pty')) return;
  isPtyMode.value = mode === 'pty';

  await nextTick();
  fitTerminals();

  if (isPtyMode.value) {
    if (!ptyTerm) {
      initPtyTerminal();
      await nextTick();
      fitTerminals();
    }
    if (ptyTerm) {
      await hubStore.joinPtyGroup(ptyTerm.cols, ptyTerm.rows);
      ptyTerm.focus();
    }
  } else {
    await hubStore.leavePtyGroup();
    fitTerminals();
    logTerm?.focus();
  }
};

// 监听 PTY 配置状态：一旦启用 PTY，默认进入左侧“终端”模式（若用户在当前页面未手动点击日志）
watch(
  isPtyConfigured,
  async (configured) => {
    if (configured) {
      if (!userManuallySwitched) {
        await toggleTerminalMode('pty');
      }
    } else {
      await toggleTerminalMode('log');
    }
  },
  { immediate: true },
);

// 连接 Store
const connectStore = async () => {
  if (!props.serverId) return;

  // 订阅系统控制台消息
  if (cleanupSystemMsg) cleanupSystemMsg();
  cleanupSystemMsg = hubStore.onSystemMessage((msg) => {
    logTerm?.writeln(msg);
    ptyTerm?.writeln(msg);
  });

  // 订阅日志
  if (cleanupLog) cleanupLog();
  cleanupLog = hubStore.onLog((msg) => {
    const coloredMsg = colorizeLog(msg);
    logTerm?.writeln(coloredMsg);

    const isDaemonNotice =
      msg.startsWith('[MSLX') ||
      msg.startsWith('>>>') ||
      msg.startsWith('[System]') ||
      msg.startsWith('[RCON]');

    if (isDaemonNotice) {
      if (ptyTerm) {
        const prefix = (ptyTerm.buffer?.active?.cursorX ?? 0) > 0 ? '\r\n' : '\r';
        ptyTerm.writeln(`${prefix}${coloredMsg}`);
      }
      if (msg.includes('停止') || msg.includes('退出') || msg.includes('强制结束')) {
        if (ptyTerm) ptyTerm.options.cursorBlink = false;
      } else if (msg.includes('启动') || msg.includes('已通过 PTY 启动')) {
        if (ptyTerm) ptyTerm.options.cursorBlink = true;
      }
      emits('update');
    }
  });

  // 订阅 PTY 原始数据流
  if (cleanupPtyData) cleanupPtyData();
  cleanupPtyData = hubStore.onPtyData((chunk) => {
    ptyTerm?.write(colorizePtyChunk(chunk));
  });

  // 订阅 PTY 状态
  if (cleanupPtyStatus) cleanupPtyStatus();
  cleanupPtyStatus = hubStore.onPtyStatus((status) => {
    const wasRunning = serverPtyStatus.value.isRunning;
    serverPtyStatus.value = status;
    if (ptyTerm) {
      ptyTerm.options.cursorBlink = !!status.isRunning;
      // 当服务端变为运行中状态时，即时适配并同步一次当前终端尺寸，防止因初始窗口尺寸偏差导致光标/提示错位
      if (status.isRunning && (!wasRunning || isPtyMode.value)) {
        fitTerminals();
        if (ptyTerm.cols > 0 && ptyTerm.rows > 0) {
          hubStore.resizePty(ptyTerm.cols, ptyTerm.rows);
        }
      }
    }
  });

  // 订阅指令结果反馈
  if (cleanupCmdResult) cleanupCmdResult();
  cleanupCmdResult = hubStore.onCommandResult((success, msg) => {
    if (!success) {
      const feedback = `\x1b[1;31m[System] 指令执行反馈: ${msg}\x1b[0m`;
      logTerm?.writeln(feedback);
      ptyTerm?.writeln(feedback);
    }
  });

  // 发起连接
  await hubStore.connect(props.serverId);

  // 如果初始处于 PTY 模式，加入 PTY 会话组（先 fit 确保尺寸真实准确）
  if (isPtyMode.value && ptyTerm) {
    fitTerminals();
    await hubStore.joinPtyGroup(ptyTerm.cols, ptyTerm.rows);
  }
};

const disconnectStore = async () => {
  // 取消回调订阅
  if (cleanupSystemMsg) cleanupSystemMsg();
  if (cleanupLog) cleanupLog();
  if (cleanupCmdResult) cleanupCmdResult();
  if (cleanupPtyData) cleanupPtyData();
  if (cleanupPtyStatus) cleanupPtyStatus();

  if (isPtyMode.value) {
    await hubStore.leavePtyGroup();
  }

  // 告知 Store 本组件退出
  await hubStore.disconnect();
};

const writeln = (msg: string) => {
  logTerm?.writeln(msg);
  ptyTerm?.writeln(msg);
};

const clear = () => {
  logTerm?.clear();
  writeWelcomeMsg();
  ptyTerm?.clear();
  writePtyWelcomeMsg();
};

// 移动端下触控滚动逻辑
let touchStartY = 0;

const handleTouchStart = (e: TouchEvent) => {
  touchStartY = e.touches[0].clientY;
};

const handleTouchMove = (e: TouchEvent) => {
  const currentTerm = isPtyMode.value ? ptyTerm : logTerm;
  if (!currentTerm) return;

  const touchCurrentY = e.touches[0].clientY;
  const deltaY = touchStartY - touchCurrentY;

  const lineHeight = 19.6;
  const linesToScroll = Math.trunc(deltaY / lineHeight);

  if (Math.abs(linesToScroll) >= 1) {
    currentTerm.scrollLines(linesToScroll);
    touchStartY = touchCurrentY + (deltaY % lineHeight);
  }
};

defineExpose({ writeln, clear });

// 监听 ServerId 变化
watch(
  () => props.serverId,
  async (newVal, oldVal) => {
    if (newVal !== oldVal) {
      userManuallySwitched = false;
      await disconnectStore();
      logTerm?.clear();
      writeWelcomeMsg();
      if (ptyTerm) {
        ptyTerm.clear();
        writePtyWelcomeMsg();
      }
      await connectStore();
    }
  },
);

onMounted(async () => {
  await nextTick();
  loadHistory();
  initLogTerminal();
  initPtyTerminal();

  themeObserver = new MutationObserver(updateTerminalTheme);
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['theme-mode', 'style'] });

  resizeObserver = new ResizeObserver(() => window.requestAnimationFrame(fitTerminals));
  if (terminalWrapper.value) {
    resizeObserver.observe(terminalWrapper.value);
  }

  await nextTick();
  fitTerminals();

  await connectStore();
  window.requestAnimationFrame(fitTerminals);
});

onUnmounted(async () => {
  themeObserver?.disconnect();
  resizeObserver?.disconnect();
  logTerm?.dispose();
  ptyTerm?.dispose();
  logTerm = null;
  ptyTerm = null;
  logFitAddon = null;
  ptyFitAddon = null;
  await disconnectStore();
});
</script>

<template>
  <div
    ref="terminalWrapper"
    class="terminal-wrapper flex-1 flex flex-col bg-[var(--td-bg-color-container)]/80 border border-[var(--td-component-border)] rounded-xl overflow-hidden shadow-sm relative w-full h-full"
  >
    <div
      class="h-[38px] shrink-0 bg-transparent border-b border-[var(--td-component-border)] flex items-center justify-between px-3 sm:px-4 relative z-10 select-none gap-2"
    >
      <div class="flex items-center gap-2 sm:gap-3 min-w-0 flex-1">
        <div class="flex gap-1.5 shrink-0 mr-1 sm:mr-2">
          <span class="w-2.5 h-2.5 rounded-full bg-[#ff5f56]"></span>
          <span class="w-2.5 h-2.5 rounded-full bg-[#ffbd2e]"></span>
          <span class="w-2.5 h-2.5 rounded-full bg-[#27c93f]"></span>
        </div>
        <div class="text-[var(--td-text-color-secondary)] text-xs font-mono truncate">
          MSLX 控制台 | #{{ serverId }}
        </div>
      </div>

      <!-- 模式切换控制器（仅在实例启用 PTY 时展示） -->
      <div
        v-if="isPtyConfigured"
        class="shrink-0 flex items-center bg-zinc-200/50 dark:bg-zinc-800/60 p-0.5 rounded-lg border border-[var(--td-component-border)] whitespace-nowrap"
      >
        <button
          :class="isPtyMode ? 'bg-white dark:bg-zinc-700 text-[var(--color-primary)] font-medium shadow-xs' : 'text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200'"
          class="px-2.5 py-0.5 text-xs rounded-md transition-all cursor-pointer whitespace-nowrap"
          @click="toggleTerminalMode('pty', true)"
        >
          终端
        </button>
        <button
          :class="!isPtyMode ? 'bg-white dark:bg-zinc-700 text-[var(--color-primary)] font-medium shadow-xs' : 'text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200'"
          class="px-2.5 py-0.5 text-xs rounded-md transition-all cursor-pointer whitespace-nowrap"
          @click="toggleTerminalMode('log', true)"
        >
          日志视图
        </button>
      </div>
    </div>

    <!-- 日志视图容器 -->
    <div
      v-show="!isPtyMode"
      ref="logTerminalBody"
      class="absolute top-[38px] bottom-[50px] left-0 right-0 pt-2 pb-1.5 pl-2.5 z-[1] terminal-body-container overflow-hidden"
      @touchstart="handleTouchStart"
      @touchmove.prevent="handleTouchMove"
    ></div>

    <!-- PTY 交互终端容器 -->
    <div
      v-show="isPtyMode"
      ref="ptyTerminalBody"
      class="absolute top-[38px] bottom-2.5 left-0 right-0 pt-2 pb-1 pl-2.5 z-[1] terminal-body-container overflow-hidden"
      @touchstart="handleTouchStart"
      @touchmove.prevent="handleTouchMove"
    ></div>

    <div
      v-if="!isPtyMode"
      class="absolute bottom-0 left-0 right-0 h-[50px] flex items-center px-4 bg-transparent border-t border-[var(--td-component-border)] z-10 gap-3"
    >
      <input
        v-model="inputCommand"
        class="flex-1 h-8 bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200 dark:border-zinc-700 rounded-md px-3 text-[var(--td-text-color-primary)] font-mono text-[13px] outline-none transition-all focus:border-[var(--color-primary)] focus:bg-white dark:focus:bg-zinc-900 placeholder:text-zinc-400 dark:placeholder:text-zinc-500"
        placeholder="发送控制台指令..."
        @keydown.up.prevent="handleHistoryUp"
        @keydown.down.prevent="handleHistoryDown"
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
  overflow: hidden !important;

  :deep(.xterm),
  :deep(.xterm-viewport),
  :deep(.xterm-screen),
  :deep(.xterm-scrollable-element) {
    background-color: transparent !important;
    touch-action: none;
  }

  :deep(.xterm-viewport) {
    overflow-x: hidden !important;
    overflow-y: hidden !important;
  }

  :deep(.xterm-scrollable-element) {
    overflow-x: hidden !important;
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
