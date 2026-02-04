import c from 'ansi-colors';
import { useWebpanelStore } from '@/store/modules/webpanel';

const webpanelStore = useWebpanelStore();

c.enabled = true;

/**
 * 日志染色工具
 * @param log 日志原始文本
 * @param mode 模式 0=不染色 1=简约染色 2=高级染色
 */
const colorizeServerLog = (log: string, mode: number = -1): string => {
  if (!log) return '';

  if (mode === -1) {
    mode = webpanelStore.settings.webPanelColorizeLogLevel;
  }

  if (mode === 0) return log;

  log = log.replace(/\n/g, '\r\n');

  // === 两种模式的统一处理前缀和特殊语句 ===

  // 优先处理特殊句式 (Done)
  if (log.includes('Done') && log.includes('!')) {
    log = log.replace(/Done \((.*?)\)!/g, (match, time) => `${c.green.bold('Done')} (${c.blue(time)})!`);
  }

  // 启动器/系统前缀处理
  if (log.startsWith('[System]')) log = log.replace(/^\[System]/, `[${c.blue.bold('System')}]`);
  if (log.includes('[MSLX]')) log = log.replace(/\[MSLX]/g, `[${c.magenta.bold('MSLX')}]`);
  if (log.includes('[MSLX-Backup]')) log = log.replace(/\[MSLX-Backup]/g, `[${c.magenta.bold('MSLX-Backup')}]`);
  if (log.includes('[MSLX-Daemon]')) log = log.replace(/\[MSLX-Daemon]/g, `[${c.magenta.bold('MSLX-Daemon')}]`);
  if (log.startsWith('>>>')) log = log.replace(/^>>>/, c.red.bold('>>>'));

  // === 简约染色 ===
  if (mode === 1) {
    // 核心格式 [21:17:09 INFO] -> 整体根据等级变色
    log = log.replace(/^\[\d{2}:\d{2}:\d{2}\s+(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/, (match, level) => {
      switch (level) {
        case 'INFO':
          return c.green(match); // [21:17:09 INFO] 全绿
        case 'WARN':
        case 'WARNING':
          return c.yellow(match); // [21:17:09 WARN] 全黄
        case 'ERROR':
        case 'FATAL':
          return c.red(match); // [21:17:09 ERROR] 全红
        case 'DEBUG':
          return c.blue(match); // [21:17:09 DEBUG] 全蓝
        default:
          return match;
      }
    });

    // 兼容格式 [22:45:08] [Server thread/WARN] -> 整体根据等级变色
    log = log.replace(/^\[\d{2}:\d{2}:\d{2}\]\s+\[[^/]+\/(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/, (match, level) => {
      switch (level) {
        case 'INFO':
          return c.green(match);
        case 'WARN':
        case 'WARNING':
          return c.yellow(match);
        case 'ERROR':
        case 'FATAL':
          return c.red(match);
        default:
          return match;
      }
    });

    // 插件/组件名称
    log = log.replace(/(?<=:\s|^)\s*([([][a-zA-Z0-9_\-.\s]+[)\]])(?=\s)/g, (match) => c.cyan(match));

    return log;
  }

  // === 高级染色 ===

  // 原本已经包含了ANSI颜色 那么只进行简单url染色
  // eslint-disable-next-line no-control-regex
  if (/\u001b\[[\d;]*m/.test(log)) {
    log = log.replace(/(https?:\/\/[^\s]+)/g, (match) => c.blue.underline(match));
    return log;
  }

  // 核心格式 [Time Level]:
  log = log.replace(/^\[(\d{2}:\d{2}:\d{2})\s+(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]:/, (_, time, level) => {
    let levelColor = level;
    switch (level) {
      case 'INFO':
        levelColor = c.green('INFO');
        break;
      case 'WARN':
      case 'WARNING':
        levelColor = c.yellow.bold('WARN');
        break;
      case 'ERROR':
      case 'FATAL':
        levelColor = c.red.bold(level);
        break;
      case 'DEBUG':
        levelColor = c.blue('DEBUG');
        break;
    }
    return `[${c.gray(time)} ${levelColor}]:`;
  });

  // 原版格式兼容 [Time] [Thread/Level]
  if (/^\[\d{2}:\d{2}:\d{2}\]/.test(log)) {
    log = log.replace(/^\[(\d{2}:\d{2}:\d{2})\]/, (match, time) => `[${c.gray(time)}]`);
  }
  log = log.replace(/\[([^/]+)\/(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/g, (_, thread, level) => {
    const threadColor = c.blue(thread);
    let levelColor = level;
    switch (level) {
      case 'INFO':
        levelColor = c.green('INFO');
        break;
      case 'WARN':
        levelColor = c.yellow('WARN');
        break;
      case 'ERROR':
        levelColor = c.red.bold('ERROR');
        break;
    }
    return `[${threadColor}/${levelColor}]`;
  });

  // 组件名称 [Component]
  log = log.replace(/(?<=:\s|^)\s*\[([a-zA-Z0-9_\-.\s]+)\](?=\s)/g, (match, content) => {
    if (content === 'System' || content.includes('MSLX')) return match;
    return ` [${c.bold.black(content)}]`;
  });

  // URL 高亮
  log = log.replace(/(https?:\/\/[^\s]+)/g, (match) => c.blue.underline(match));

  // 版本号高亮
  log = log.replace(/\b\d+\.\d+[\w.+\-@]*(?<!ms|s|MB|GB|KB|%)\b/g, (match) => c.magenta(match));

  // 单位与数字
  log = log.replace(/\b\d+(\.\d+)?\s?(ms|s|%|MB|GB|KB)\b/gi, (match) => c.blue(match));

  // 纯数字高亮
  // eslint-disable-next-line no-control-regex
  log = log.replace(
    /(\u001b\[[\d;]*m)|((?<!\d:\d)(?<![.\-+])\b\d+\b(?![.\-+])(?!\s*:\s*\d))/g,
    (match, ansi, number) => {
      if (ansi) return match; // 保护原有的颜色代码
      if (number) {
        // 端口号(4-6位) 和 小数量(<=3位) 染蓝
        if (number.length >= 4 && number.length <= 6) return c.blue(number);
        if (number.length <= 3) return c.blue(number);
      }
      return match;
    },
  );

  // IP/端口匹配 (*:25565)
  log = log.replace(/(\*:\d{1,5})/, (match) => c.blue.bold(match));

  // 关键词状态高亮
  log = log.replace(/\b(Loaded|Saved|Starting|Started|Connected)\b/g, (match) => c.green(match));
  // eslint-disable-next-line no-control-regex
  log = log.replace(/\bDone\b(?!\u001b)/g, c.green.bold('Done'));

  log = log.replace(/\b(Failed|Exception|Error|Caused by|Stopping|Closed)\b/g, (match) => c.red.bold(match));
  log = log.replace(/\b(Loading|Preparing|Generating|Saving|Using|Running)\b/g, (match) => c.magenta(match));

  log = log.replace(/\b(Minecraft|Paper|Velocity|Java)\b/gi, (match) => c.bold.black(match));
  log = log.replace(/'minecraft:[a-z_]+'/g, (match) => c.magenta(match));

  return log;
};;;;

export default colorizeServerLog;
