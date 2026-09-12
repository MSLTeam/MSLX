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

  // 优先处理特殊句式 (Done/Started)
  if (log.includes('Done') && log.includes('!')) {
    log = log.replace(/Done \((.*?)\)!/g, (match, time) => `${c.green.bold('Done')} (${c.blue(time)})!`);
  }

  // 基岩版关键词处理
  log = log.replace(/\b(Server started|Starting Server)\b/g, (match) => c.green.bold(match));
  log = log.replace(/\b(IPv4 supported|IPv6 supported)\b/g, (match) => c.cyan(match));

  // 启动器/系统前缀处理
  if (log.startsWith('[System]')) log = log.replace(/^\[System\]/, `[${c.blue.bold('System')}]`);
  if (log.includes('[MSLX-Backup]')) log = log.replace(/\[MSLX-Backup\]/g, `[${c.yellow.bold('MSLX-Backup')}]`);
  if (log.includes('[MSLX-Daemon]')) log = log.replace(/\[MSLX-Daemon\]/g, `[${c.blue.bold('MSLX-Daemon')}]`);
  if (log.includes('[MSLX-MCServer]')) log = log.replace(/\[MSLX-MCServer\]/g, `[${c.cyan.bold('MSLX-MCServer')}]`);
  if (log.includes('[MSLX]')) log = log.replace(/\[MSLX\]/g, `[${c.cyan.bold('MSLX')}]`);
  if (log.includes('[RCON]')) log = log.replace(/\[RCON\]/g, `[${c.green.bold('RCON')}]`);

  // >>> 前缀根据语义染色（默认品牌青色，警告黄色，错误红色）
  if (log.startsWith('>>>')) {
    if (/\b(error|failed|exception|失败|错误|无效|❌)\b/i.test(log)) {
      log = log.replace(/^>>>/, c.red.bold('>>>'));
    } else if (/\b(warn|warning|警告|⚠️)\b/i.test(log)) {
      log = log.replace(/^>>>/, c.yellow.bold('>>>'));
    } else {
      log = log.replace(/^>>>/, c.cyan.bold('>>>'));
    }
  }

  // 针对 MSLX / System 体系的生命周期与状态关键词染色
  if (log.includes('[MSLX') || log.includes('[System]') || log.startsWith('>>>')) {
    // 启动成功
    log = log.replace(
      /(服务器进程已通过 PTY 启动|服务器进程已启动|启动成功)/g,
      (match) => c.green.bold(match),
    );
    // 关停/停止
    log = log.replace(
      /(服务器已停止|服务器进程已停止|停止处理完成|已强制结束[^\s,，。]*)/g,
      (match) => c.yellow.bold(match),
    );
    // 进行中动作
    log = log.replace(
      /(正在初始化服务|正在启动服务端实例|正在执行重启|正在处理下载[^\s,，。]*)/g,
      (match) => c.cyan(match),
    );
    log = log.replace(
      /(准备执行停止指令|已发送关闭指令[^\s,，。]*|正在等待[^\s,，。]*|正在备份[^\s,，。]*)/g,
      (match) => c.yellow(match),
    );
    // 状态标签
    log = log.replace(/\(用户操作\)/g, c.yellow('(用户操作)'));
    log = log.replace(/\(正常关闭\)/g, c.green('(正常关闭)'));
    log = log.replace(/\(异常退出\)/g, c.red.bold('(异常退出)'));
    // PID 与退出码
    log = log.replace(/\bPID:\s*(\d+)/g, (_, pid) => `PID: ${c.cyan(pid)}`);
    log = log.replace(/退出代码:\s*(\d+)/g, (_, code) => `退出代码: ${code === '0' ? c.green(code) : c.red.bold(code)}`);
  }

  // === 简约染色 ===
  if (mode === 1) {
    // 核心格式 [21:17:09 INFO] -> 整体根据等级变色
    log = log.replace(/^\[[^\]]+\s+(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/, (match, level) => {
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
    // 判断是否为错误日志的函数
    const isErrorLog = (log) => {
      // eslint-disable-next-line no-control-regex
      const hasAnsiRed = /\u001b\[(0;)?31m/.test(log); // 检查是否包含红色代码
      const hasErrorKeyword = /\b(ERROR|Exception|Caused by|at)\b/i.test(log); // 检查报错关键字
      return hasAnsiRed || hasErrorKeyword;
    };

    if (!isErrorLog(log)) {
      log = log.replace(/(?<=:\s|^)\s*([([][a-zA-Z0-9_\-.\s]+[)\]])(?=\s)/g, (match) => c.cyan(match));
    }

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
  log = log.replace(
    // eslint-disable-next-line no-control-regex
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
};

export default colorizeServerLog;
