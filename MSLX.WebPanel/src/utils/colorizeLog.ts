import c from 'ansi-colors';

c.enabled = true;

const colorizeServerLog = (log: string): string => {
  if (!log) return '';

  // 时间
  log = log.replace(/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z?)/, (match) => c.gray(match));

  // MC 短时间
  log = log.replace(/^\[(\d{2}:\d{2}:\d{2})\]/, (_, time) =>
    `[${c.gray(time)}${c.blue(']')}`
  );

  // 特殊前缀 (MSLX)
  if (log.startsWith('[MSLX]')) {
    log = log.replace(/^\[MSLX\]/, ` [${c.magenta.bold('MSLX')}]`);
  }

  // 核心结构: [线程/等级]
  log = log.replace(/\[([^/]+)\/(INFO|WARN|WARNING|ERROR|FATAL|DEBUG)\]/g, (_, thread, level) => {
    const threadColor = c.magenta(thread); // 线程名保持品红
    let levelColor = level;

    switch (level) {
      case 'INFO': levelColor = c.green('INFO'); break;
      case 'WARN':
      case 'WARNING': levelColor = c.yellow('WARN'); break;
      case 'ERROR':
      case 'FAIL':
      case 'FATAL': levelColor = c.red.bold(level); break;
      case 'DEBUG': levelColor = c.cyan('DEBUG'); break;
    }
    return `${c.blue('[')}${threadColor}${c.gray('/')}${levelColor}${c.blue(']')}`;
  });

  // 类名/来源
  log = log.replace(/ \[([^\]/]+\/[^\]]+)\]:?/, (match, source) => {
    const hasColon = match.endsWith(':');
    return ` ${c.blue('[')}${c.gray(source)}${c.blue(']')}${hasColon ? ': ' : ' '}`;
  });

  // 关键字高亮
  log = log.replace(/\b\d+(\.\d+)?(ms|s|%)\b/g, (match) => c.blue(match)); // 单位改成蓝色，更清楚
  log = log.replace(/(?<=PID:\s)\d+/g, (match) => c.blue(match));

  if (log.includes('Done')) {
    log = log.replace(/Done \((.*?)\)!/, `${c.green.bold('Done')} (${c.blue('$1')})!`);
  }

  log = log.replace(/\b(Starting)\b/g, c.blue('Starting')); // 动作词改深蓝
  log = log.replace(/\b(Stopping)\b/g, c.red('Stopping'));
  log = log.replace(/\b(Saved)\b/g, c.green('Saved'));
  log = log.replace(/\b(Saving)\b/g, c.magenta('Saving'));
  log = log.replace(/\b(Loaded)\b/g, c.green('Loaded'));
  log = log.replace(/\b(Failed)\b/g, c.red.bold('Failed'));
  log = log.replace(/\b(Exception)\b/g, c.red.bgBlack('Exception'));
  log = log.replace(/(\*:\d{1,5})/, c.underline.blue('$1')); // 端口号深蓝

  log = log.replace(/\n/g, '\r\n');

  return log;
};

export default colorizeServerLog;
