import c from 'ansi-colors';

c.enabled = true;

const colorizeServerLog = (log: string): string => {
  if (!log) return '';

  // 优先处理特殊句式 (Done)
  if (log.includes('Done') && log.includes('!')) {
    log = log.replace(/Done \((.*?)\)!/g, (match, time) =>
      `${c.green.bold('Done')} (${c.blue(time)})!`
    );
  }

  // 启动器/系统前缀处理
  if (log.startsWith('[System]')) log = log.replace(/^\[System\]/, `[${c.blue.bold('System')}]`);
  if (log.includes('[MSLX]')) log = log.replace(/\[MSLX\]/g, `[${c.magenta.bold('MSLX')}]`);
  if (log.startsWith('>>>')) log = log.replace(/^>>>/, c.red.bold('>>>'));

  // 核心格式 [Time Level]:
  log = log.replace(/^\[(\d{2}:\d{2}:\d{2})\s+(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]:/, (_, time, level) => {
    let levelColor = level;
    switch (level) {
      case 'INFO': levelColor = c.green('INFO'); break;
      case 'WARN':
      case 'WARNING': levelColor = c.magenta.bold('WARN'); break;
      case 'ERROR':
      case 'FATAL': levelColor = c.red.bold(level); break;
      case 'DEBUG': levelColor = c.blue('DEBUG'); break;
    }
    return `[${c.gray(time)} ${levelColor}]:`;
  });

  // 原版格式兼容
  if (/^\[\d{2}:\d{2}:\d{2}\]/.test(log)) {
    log = log.replace(/^\[(\d{2}:\d{2}:\d{2})\]/, (match, time) => `[${c.gray(time)}]`);
  }
  log = log.replace(/\[([^/]+)\/(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/g, (_, thread, level) => {
    const threadColor = c.blue(thread);
    let levelColor = level;
    switch (level) {
      case 'INFO': levelColor = c.green('INFO'); break;
      case 'WARN': levelColor = c.magenta('WARN'); break;
      case 'ERROR': levelColor = c.red.bold('ERROR'); break;
    }
    return `[${threadColor}/${levelColor}]`;
  });

  // 组件名称 [Component]
  log = log.replace(/(?<=:\s|^)\s*\[([a-zA-Z0-9_\-.\s]+)\](?=\s)/g, (match, content) => {
    if (content === 'System' || content.includes('MSLX')) return match;
    return ` [${c.bold.black(content)}]`;
  });

  // 数据类型高亮 (URL/版本/单位/数字/端口)
  log = log.replace(/(https?:\/\/[^\s]+)/g, (match) => c.blue.underline(match));
  log = log.replace(/\b\d+\.\d+(\.\d+)?(-[a-zA-Z0-9]+)?\b/g, (match) => c.magenta(match));

  // 单位与数字
  log = log.replace(/\b\d+(\.\d+)?\s?(ms|s|%|MB|GB|KB)\b/gi, (match) => c.blue(match));
  log = log.replace(/(?<!\d:\d)\b\d+\b(?!\s*:\s*\d)/g, (match) => {
    if (match.length >= 4 && match.length <= 6) return c.blue(match); // 端口/PID
    if (match.length <= 3) return c.blue(match); // 数量
    return match;
  });
  log = log.replace(/(\*:\d{1,5})/, (match) => c.blue.bold(match));

  // 关键词状态高亮
  log = log.replace(/\b(Loaded|Saved|Starting|Started|Connected)\b/g, (match) => c.green(match));
  log = log.replace(/\bDone\b(?!\u001b)/g, c.green.bold('Done')); // eslint-disable-line no-control-regex

  log = log.replace(/\b(Failed|Exception|Error|Caused by|Stopping|Closed)\b/g, (match) => c.red.bold(match));
  log = log.replace(/\b(Loading|Preparing|Generating|Saving|Using|Running)\b/g, (match) => c.magenta(match));

  log = log.replace(/\b(Minecraft|Paper|Velocity|Java)\b/gi, (match) => c.bold.black(match));
  log = log.replace(/'minecraft:[a-z_]+'/g, (match) => c.magenta(match));

  // 换行符标准化
  log = log.replace(/\n/g, '\r\n');

  return log;
};

export default colorizeServerLog;
