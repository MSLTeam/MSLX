export interface CommandPreset {
  label: string;
  desc: string;
  command: string;
}

export const COMMON_COMMAND_PRESETS: CommandPreset[] = [
  {
    label: '全屏大标题公告',
    desc: '在玩家屏幕中央醒目显示副标题与红字大标题',
    command: 'title @a times 10 70 20\ntitle @a subtitle {"text":"请注意规划您的游戏进度哦～","color":"yellow"}\ntitle @a title {"text":"服务器即将进行维护","color":"red","bold":true}',
  },
  {
    label: '彩色聊天框公告',
    desc: '向所有在线玩家聊天框发送醒目的金色公告信息',
    command: 'tellraw @a {"text":"[服务器公告] 服务器即将进行例行维护，请注意规划您的游戏进度哦～","color":"gold","bold":true}',
  },
  {
    label: '标准广播通知',
    desc: '使用控制台标准 say 命令向全服发送通知',
    command: 'say [系统广播] 每日定时备份与资源优化即将进行。',
  },
  {
    label: '清理地面掉落物',
    desc: '清除地面掉落物品以减轻服务器负担',
    command: 'kill @e[type=item]',
  },
  {
    label: '强制世界存档',
    desc: '将内存中所有区块数据强制立即写入硬盘',
    command: 'save-all flush',
  },
];

/**
 * 将给定的 Cron 表达式向前推算 minutesBefore 分钟
 * 支持：
 * 1. 固定时分：0 0 4 * * ? 提前 10 分钟 -> 0 50 3 * * ?
 * 2. 每小时固定分钟：0 0 * * * ? 提前 10 分钟 -> 0 50 * * * ?
 * 3. 间隔小时：0 0 * / 10 * * ? 提前 10 分钟 -> 0 50 9,19,23 * * ?
 * 4. 间隔分钟：0 * / 30 * * * ? 提前 10 分钟 -> 0 20,50 * * * ?
 * 规则不满足前推条件或周期过短时返回 null，调用方不可回退为原表达式
 */
export function shiftCronMinutes(cronStr: string, minutesBefore: number): string | null {
  if (!cronStr || minutesBefore <= 0) return null;
  const parts = cronStr.trim().split(/\s+/);
  if (parts.length < 5 || parts.length > 6) return null;

  const is6 = parts.length === 6;
  const sec = is6 ? parts[0] : '0';
  const minStr = is6 ? parts[1] : parts[0];
  const hourStr = is6 ? parts[2] : parts[1];
  const day = is6 ? parts[3] : parts[2];
  const month = is6 ? parts[4] : parts[3];
  const week = is6 ? parts[5] : parts[4];

  const min = parseInt(minStr, 10);
  const hour = parseInt(hourStr, 10);

  // 1. 固定分钟 + 固定小时 (例如: 0 0 4 * * ?)
  if (!isNaN(min) && !isNaN(hour) && !hourStr.includes('/') && !minStr.includes('/')) {
    let totalMinutes = hour * 60 + min - minutesBefore;
    while (totalMinutes < 0) {
      totalMinutes += 24 * 60;
    }
    const newHour = Math.floor(totalMinutes / 60) % 24;
    const newMin = totalMinutes % 60;

    return is6
      ? `${sec} ${newMin} ${newHour} ${day} ${month} ${week}`
      : `${newMin} ${newHour} ${day} ${month} ${week}`;
  }

  // 2. 固定分钟 + 每小时通配符 * (例如: 0 0 * * * ?)
  if (!isNaN(min) && !minStr.includes('/') && hourStr === '*') {
    let newMin = (min - minutesBefore) % 60;
    if (newMin < 0) newMin += 60;
    return is6
      ? `${sec} ${newMin} * ${day} ${month} ${week}`
      : `${newMin} * ${day} ${month} ${week}`;
  }

  // 3. 固定分钟 + 间隔小时 (例如: 0 0 */10 * * ? 或 0 0 */12 * * ?)
  if (!isNaN(min) && !minStr.includes('/') && hourStr.startsWith('*/')) {
    const step = parseInt(hourStr.slice(2), 10);
    if (!isNaN(step) && step > 0 && step <= 24) {
      // 一天内的触发小时点
      const triggerHours: number[] = [];
      for (let h = 0; h < 24; h += step) {
        triggerHours.push(h);
      }

      // 计算每一个小时点提前 minutesBefore 分钟后的对应小时
      let shiftedMin = 0;
      const shiftedHoursSet = new Set<number>();
      for (const h of triggerHours) {
        let totalMinutes = h * 60 + min - minutesBefore;
        while (totalMinutes < 0) {
          totalMinutes += 24 * 60;
        }
        shiftedMin = totalMinutes % 60;
        shiftedHoursSet.add(Math.floor(totalMinutes / 60) % 24);
      }

      const sortedHours = Array.from(shiftedHoursSet).sort((a, b) => a - b);
      const newHourStr = sortedHours.join(',');
      return is6
        ? `${sec} ${shiftedMin} ${newHourStr} ${day} ${month} ${week}`
        : `${shiftedMin} ${newHourStr} ${day} ${month} ${week}`;
    }
  }

  // 4. 间隔分钟 (例如: 0 */30 * * * ? 且提前量小于间隔)
  if (minStr.startsWith('*/')) {
    const step = parseInt(minStr.slice(2), 10);
    if (!isNaN(step) && step > minutesBefore && step <= 60) {
      // 触发分钟点
      const triggerMins: number[] = [];
      for (let m = 0; m < 60; m += step) {
        triggerMins.push(m);
      }
      const shiftedMins = triggerMins
        .map((m) => {
          let sm = m - minutesBefore;
          if (sm < 0) sm += 60;
          return sm;
        })
        .sort((a, b) => a - b);

      const newMinStr = shiftedMins.join(',');
      return is6
        ? `${sec} ${newMinStr} ${hourStr} ${day} ${month} ${week}`
        : `${newMinStr} ${hourStr} ${day} ${month} ${week}`;
    }
  }

  return null;
}

/**
 * 根据预设类型与提前分钟数，生成游戏内提醒通知指令
 * 优先兼容全版本通用 say 控制台广播，避免非 MC Java 服务器报错
 */
export function generateAdvanceNoticePayload(
  templateType: 'title' | 'tellraw' | 'say',
  minutes: number,
  taskType?: string,
  taskName?: string
) {
  const isRestart = taskType?.toLowerCase() === 'restart';
  const isStop = taskType?.toLowerCase() === 'stop';
  const actionText = isRestart ? '进行例行重启' : isStop ? '进行停服维护' : `执行 [${taskName || '计划任务'}]`;

  if (templateType === 'title') {
    return `title @a times 10 70 20\ntitle @a subtitle {"text":"请注意保存个人物品并做好准备","color":"yellow"}\ntitle @a title {"text":"服务器将在 ${minutes} 分钟后${actionText}","color":"red","bold":true}`;
  }
  if (templateType === 'tellraw') {
    return `tellraw @a {"text":"[系统通知] 服务器将在 ${minutes} 分钟后${actionText}，请规划好您的游戏进度哦～","color":"gold","bold":true}`;
  }
  // 默认控制台标准广播 say
  return `say [系统广播] 服务器将在 ${minutes} 分钟后${actionText}，请规划好您的游戏进度哦～`;
}
