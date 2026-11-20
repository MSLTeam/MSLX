<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { CheckCircleIcon, ErrorCircleIcon, TimeIcon } from 'tdesign-icons-vue-next';
import { CronExpressionParser } from 'cron-parser';

interface ExecutionItem {
  formattedTime: string;
  weekday: string;
}

const props = defineProps({
  visible: { type: Boolean, default: false },
  initialValue: { type: String, default: '' },
});

const emits = defineEmits(['update:visible', 'confirm']);

// --- 模式管理 ---
const mode = ref<'preset' | 'novice' | 'pro'>('preset');
const isManualEdit = ref(false);
const customCron = ref('*/10 * * * * *');

// --- 预设模式数据 ---
const selectedPreset = ref('*/10 * * * * *'); // 默认选中第一条
const presetOptions = [
  { label: '每 10 秒', value: '*/10 * * * * *' },
  { label: '每分钟', value: '0 * * * * *' },
  { label: '每 5 分钟', value: '0 */5 * * * *' },
  { label: '每 30 分钟', value: '0 */30 * * * *' },
  { label: '每小时', value: '0 0 * * * ?' },
  { label: '每天凌晨 3 点', value: '0 0 3 * * ?' },
  { label: '每周日凌晨 3 点', value: '0 0 3 ? * 0' },
  { label: '每周一凌晨 3 点', value: '0 0 3 ? * 1' },
];

const applyPreset = (val: string) => {
  selectedPreset.value = val;
  customCron.value = val;
  isManualEdit.value = false;
};

// --- 简单模式数据 (原新手模式) ---
const noviceValue = ref(10);
const noviceUnit = ref('minute');
const noviceOptions = [
  { label: '秒', value: 'second' },
  { label: '分钟', value: 'minute' },
  { label: '小时', value: 'hour' },
  { label: '天', value: 'day' },
];

// --- 专业模式数据 ---
const activeTab = ref('second');
const timeUnits = [
  { label: '秒', value: 'second', min: 0, max: 59 },
  { label: '分', value: 'minute', min: 0, max: 59 },
  { label: '时', value: 'hour', min: 0, max: 23 },
  { label: '日', value: 'day', min: 1, max: 31 },
  { label: '月', value: 'month', min: 1, max: 12 },
  { label: '周', value: 'week', min: 0, max: 6 },
];

const state = ref<Record<string, any>>({
  second: { type: 'every', start: 0, step: 1, specifics: [] },
  minute: { type: 'every', start: 0, step: 1, specifics: [] },
  hour: { type: 'every', start: 0, step: 1, specifics: [] },
  day: { type: 'every', start: 1, step: 1, specifics: [] },
  month: { type: 'every', start: 1, step: 1, specifics: [] },
  week: { type: 'every', start: 0, step: 1, specifics: [] },
});

// 遵循标准 Unix/Vixie Cron 星期映射 (0=周日, 1=周一, ..., 6=周六, 兼容 7=周日)
const weekOptions = [
  { label: '周一', value: 1 },
  { label: '周二', value: 2 },
  { label: '周三', value: 3 },
  { label: '周四', value: 4 },
  { label: '周五', value: 5 },
  { label: '周六', value: 6 },
  { label: '周日', value: 0 },
];

// --- 计算逻辑 ---
const proCron = computed(() => {
  const getVal = (unit: string, defaultVal = '*') => {
    const s = state.value[unit];
    if (!s) return defaultVal;

    // 周的处理:
    if (unit === 'week') {
      if (s.type === 'every' || s.type === 'any') {
        // 如果“日”指定了具体日期，“周”根据惯例输出 ?
        return state.value.day?.type === 'specific' ? '?' : '*';
      }
      if (s.type === 'specific') {
        if (!s.specifics || s.specifics.length === 0) return '0';
        return [...s.specifics].sort((a: number, b: number) => a - b).join(',');
      }
    }

    // 日的处理:
    if (unit === 'day') {
      // 如果“周”指定了具体星期，“日”输出 ?
      if (state.value.week?.type === 'specific') {
        return '?';
      }
    }

    if (s.type === 'every' || s.type === 'any') return '*';
    if (s.type === 'interval') return `${s.start}/${s.step}`;
    if (s.type === 'specific') {
      if (!s.specifics || s.specifics.length === 0) return unit === 'day' || unit === 'month' ? '1' : '0';
      return [...s.specifics].sort((a: number, b: number) => a - b).join(',');
    }
    return defaultVal;
  };

  const s = getVal('second');
  const m = getVal('minute');
  const h = getVal('hour');
  const d = getVal('day');
  const M = getVal('month');
  const w = getVal('week');
  return `${s} ${m} ${h} ${d} ${M} ${w}`;
});

const noviceCron = computed(() => {
  const v = noviceValue.value;
  switch (noviceUnit.value) {
    case 'second':
      return `*/${v} * * * * *`;
    case 'minute':
      return `0 */${v} * * * *`;
    case 'hour':
      return `0 0 */${v} * * ?`;
    case 'day':
      return `0 0 0 */${v} * ?`;
    default:
      return '* * * * * *';
  }
});

// 最终表达式根据当前模式和是否手动编辑决定
const finalCron = computed(() => {
  if (isManualEdit.value) return customCron.value;
  if (mode.value === 'preset') return selectedPreset.value;
  if (mode.value === 'novice') return noviceCron.value;
  return proCron.value;
});

// 当切换模式或控件变动时，同步 customCron
watch(
  [mode, selectedPreset, noviceCron, proCron],
  () => {
    if (!isManualEdit.value) {
      if (mode.value === 'preset') {
        customCron.value = selectedPreset.value;
      } else if (mode.value === 'novice') {
        customCron.value = noviceCron.value;
      } else {
        customCron.value = proCron.value;
      }
    }
  },
  { immediate: true },
);

// --- 下5次执行时间计算 ---
const nextExecutions = ref<ExecutionItem[]>([]);
const cronError = ref('');
const weekdays = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];

const calculateNextRuns = () => {
  const expr = finalCron.value?.trim();
  if (!expr) {
    cronError.value = 'Cron 表达式不能为空';
    nextExecutions.value = [];
    return;
  }

  try {
    const interval = CronExpressionParser.parse(expr, {
      currentDate: new Date(),
    });

    const items: ExecutionItem[] = [];
    const dates = interval.take(5);
    for (const d of dates) {
      const date = d.toDate();
      const year = date.getFullYear();
      const month = String(date.getMonth() + 1).padStart(2, '0');
      const day = String(date.getDate()).padStart(2, '0');
      const hours = String(date.getHours()).padStart(2, '0');
      const minutes = String(date.getMinutes()).padStart(2, '0');
      const seconds = String(date.getSeconds()).padStart(2, '0');
      const formattedTime = `${year}-${month}-${day} ${hours}:${minutes}:${seconds}`;
      const weekday = weekdays[date.getDay()];
      items.push({ formattedTime, weekday });
    }

    nextExecutions.value = items;
    cronError.value = '';
  } catch {
    cronError.value = '无法解析当前 Cron 表达式，请检查规则是否冲突';
    nextExecutions.value = [];
  }
};

watch(
  finalCron,
  () => {
    calculateNextRuns();
  },
  { immediate: true },
);

// --- 初始值反解析逻辑 ---
const parseFieldToState = (fieldStr: string, unit: string) => {
  const minDefault = unit === 'day' || unit === 'month' ? 1 : 0;
  if (fieldStr === '*' || fieldStr === '?') {
    return { type: 'every', start: minDefault, step: 1, specifics: [] };
  }
  if (fieldStr.includes('/')) {
    const [startS, stepS] = fieldStr.split('/');
    const start = startS === '*' || startS === '?' ? minDefault : parseInt(startS, 10);
    const step = parseInt(stepS, 10);
    if (!isNaN(start) && !isNaN(step)) {
      return { type: 'interval', start, step, specifics: [] };
    }
  }
  // 逗号分隔或单个数值
  const nums = fieldStr
    .split(',')
    .map((s) => {
      let n = parseInt(s.trim(), 10);
      if (unit === 'week' && n === 7) n = 0; // 容错兼容: 周日 7 转 0
      return n;
    })
    .filter((n) => !isNaN(n));

  if (nums.length > 0) {
    return { type: 'specific', start: minDefault, step: 1, specifics: nums };
  }
  return null;
};

const parseCronToState = (cronStr: string): boolean => {
  const cleanStr = cronStr.trim();
  // 检查是否与预设一致
  const foundPreset = presetOptions.find((p) => p.value === cleanStr);
  if (foundPreset) {
    mode.value = 'preset';
    selectedPreset.value = cleanStr;
    customCron.value = cleanStr;
    isManualEdit.value = false;
    return true;
  }

  const parts = cleanStr.split(/\s+/);
  if (parts.length < 5 || parts.length > 6) return false;

  const is6 = parts.length === 6;
  const secStr = is6 ? parts[0] : '0';
  const minStr = is6 ? parts[1] : parts[0];
  const hourStr = is6 ? parts[2] : parts[1];
  const dayStr = is6 ? parts[3] : parts[2];
  const monthStr = is6 ? parts[4] : parts[3];
  const weekStr = is6 ? parts[5] : parts[4];

  const parsedSec = parseFieldToState(secStr, 'second');
  const parsedMin = parseFieldToState(minStr, 'minute');
  const parsedHour = parseFieldToState(hourStr, 'hour');
  const parsedDay = parseFieldToState(dayStr, 'day');
  const parsedMonth = parseFieldToState(monthStr, 'month');
  const parsedWeek = parseFieldToState(weekStr, 'week');

  if (parsedSec && parsedMin && parsedHour && parsedDay && parsedMonth && parsedWeek) {
    state.value.second = parsedSec;
    state.value.minute = parsedMin;
    state.value.hour = parsedHour;
    state.value.day = parsedDay;
    state.value.month = parsedMonth;
    state.value.week = parsedWeek;
    mode.value = 'pro';
    isManualEdit.value = false;
    customCron.value = cleanStr;
    return true;
  }

  return false;
};

// --- 交互逻辑 ---
watch(
  () => props.visible,
  (val) => {
    if (val) {
      if (props.initialValue && props.initialValue.trim()) {
        const parsed = parseCronToState(props.initialValue.trim());
        if (!parsed) {
          // 复杂或无法分解的表达式，以自定义模式展示
          mode.value = 'pro';
          customCron.value = props.initialValue.trim();
          isManualEdit.value = true;
        }
      } else {
        // 如果没有初始值，默认进入预设模式
        mode.value = 'preset';
        customCron.value = selectedPreset.value;
        isManualEdit.value = false;
      }
      calculateNextRuns();
    }
  },
);

const handleManualCronChange = () => {
  isManualEdit.value = true;
  calculateNextRuns();
};

const handleConfirm = () => {
  if (cronError.value || !finalCron.value) {
    MessagePlugin.warning('当前表达式无效，无法保存');
    return;
  }
  emits('confirm', finalCron.value);
  emits('update:visible', false);
};

const getSpecificOptions = (unit: string) => {
  const u = timeUnits.find((t) => t.value === unit);
  if (!u) return [];
  const arr = [];
  for (let i = u.min; i <= u.max; i++) arr.push({ label: i.toString(), value: i });
  return arr;
};
</script>

<template>
  <t-dialog
    :visible="visible"
    header="Cron 表达式生成器"
    width="700px"
    top="5vh"
    attach="body"
    class="cron-gen-dialog"
    @close="emits('update:visible', false)"
    @confirm="handleConfirm"
  >
    <div class="flex flex-col gap-5 p-5 md:p-6 bg-zinc-50/50 dark:bg-zinc-950/20">

      <div class="flex justify-center">
        <t-radio-group
          v-model="mode"
          variant="default-filled"
          class="!bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-lg p-0.5 shadow-sm"
          @change="isManualEdit = false"
        >
          <t-radio-button value="preset" class="!px-4">⭐ 预设模式</t-radio-button>
          <t-radio-button value="novice" class="!px-4">🚀 简单模式</t-radio-button>
          <t-radio-button value="pro" class="!px-4">🛠️ 专业模式</t-radio-button>
        </t-radio-group>
      </div>

      <div v-if="mode === 'preset'" class="flex flex-col bg-white/80 dark:bg-zinc-800/60 border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl p-6 min-h-[260px] shadow-sm backdrop-blur-md">
        <div class="text-sm font-bold text-zinc-700 dark:text-zinc-200 mb-4">常用预设规则</div>
        <div class="grid grid-cols-2 md:grid-cols-3 gap-3">
          <t-button
            v-for="(preset, index) in presetOptions"
            :key="index"
            variant="outline"
            theme="default"
            class="!m-0 !w-full !rounded-lg !h-12 !justify-start !px-4 hover:!border-[var(--color-primary)] hover:!text-[var(--color-primary)] transition-all bg-white dark:bg-zinc-900/50"
            @click="applyPreset(preset.value)"
          >
            <div class="flex flex-col items-start gap-0.5">
              <span class="text-sm font-bold">{{ preset.label }}</span>
            </div>
          </t-button>
        </div>
      </div>

      <div v-if="mode === 'novice'" class="flex flex-col justify-center items-center bg-white/80 dark:bg-zinc-800/60 border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl p-8 min-h-[260px] shadow-sm backdrop-blur-md">
        <div class="flex items-center gap-3 text-base font-bold text-zinc-700 dark:text-zinc-200">
          <span>每隔</span>
          <t-input-number v-model="noviceValue" :min="1" theme="column" class="!w-[100px] shadow-sm" />
          <t-select v-model="noviceUnit" :options="noviceOptions" class="!w-[100px] shadow-sm" />
          <span>执行一次</span>
        </div>

        <t-alert theme="info" class="!mt-8 !rounded-lg !bg-blue-50/50 dark:!bg-blue-900/10 !border-blue-100 dark:!border-blue-800/30">
          <template #message>
            <span class="text-[var(--td-text-color-secondary)] text-xs leading-relaxed">
              此模式适用于简单的周期性任务。如果需要“每周五上午 10 点”等复杂规则，请切换到 <b class="text-[var(--td-text-color-primary)]">专业模式</b>。
            </span>
          </template>
        </t-alert>
      </div>

      <div v-if="mode === 'pro'" class="bg-white/80 dark:bg-zinc-800/60 border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl overflow-hidden shadow-sm min-h-[260px] backdrop-blur-md flex flex-col">
        <t-tabs v-model="activeTab" class="custom-tabs">
          <t-tab-panel v-for="unit in timeUnits" :key="unit.value" :value="unit.value" :label="unit.label">
            <div class="p-5 max-h-[260px] overflow-y-auto custom-scrollbar">

              <t-radio-group v-model="state[unit.value].type" direction="vertical" class="w-full gap-4 !bg-transparent" @change="isManualEdit = false">
                <t-radio value="every" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  {{ unit.value === 'week' ? '不指定 (通配符)' : `每${unit.label} (*)` }}
                </t-radio>

                <t-radio v-if="unit.value !== 'week'" value="interval" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span>周期: 从</span>
                    <t-input-number v-model="state[unit.value].start" :min="unit.min" :max="unit.max" size="small" theme="column" class="!w-[70px]" @change="isManualEdit = false" />
                    <span>{{ unit.label }} 开始，每</span>
                    <t-input-number v-model="state[unit.value].step" :min="1" :max="unit.max" size="small" theme="column" class="!w-[70px]" @change="isManualEdit = false" />
                    <span>{{ unit.label }} 执行一次</span>
                  </div>
                </t-radio>

                <t-radio v-if="unit.value !== 'week'" value="specific" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  指定: 选择具体的{{ unit.label }}
                </t-radio>

                <t-radio v-if="unit.value === 'week'" value="specific" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  指定周几
                </t-radio>
              </t-radio-group>

              <div v-if="state[unit.value].type === 'specific'" class="mt-4 p-4 bg-zinc-50 dark:bg-zinc-900/50 rounded-xl border border-zinc-100 dark:border-zinc-800 shadow-inner">
                <t-checkbox-group
                  v-model="state[unit.value].specifics"
                  :options="unit.value === 'week' ? weekOptions : getSpecificOptions(unit.value)"
                  class="grid grid-cols-[repeat(auto-fill,minmax(55px,1fr))] gap-2.5"
                  @change="isManualEdit = false"
                />
              </div>

            </div>
          </t-tab-panel>
        </t-tabs>
      </div>

      <div class="bg-[var(--td-bg-color-container)]/80 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm p-5 flex flex-col gap-4 backdrop-blur-md">

        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 border-b border-dashed border-zinc-200 dark:border-zinc-700/60">
          <span class="font-bold text-[var(--td-text-color-primary)] text-sm">当前表达式:</span>
          <div class="flex items-center gap-3">
            <t-input
              v-model="customCron"
              class="font-mono text-base font-bold tracking-wider !w-[240px] sm:!w-[280px]"
              placeholder="例如: 0 0 3 * * ?"
              @change="handleManualCronChange"
            />
            <t-tag v-if="!cronError" theme="success" variant="light" size="small" class="!rounded">
              <template #icon><check-circle-icon /></template> 有效
            </t-tag>
            <t-tag v-else theme="danger" variant="light" size="small" class="!rounded">
              <template #icon><error-circle-icon /></template> 无效
            </t-tag>
          </div>
        </div>

        <div class="flex flex-col gap-2">
          <div class="text-xs font-bold text-[var(--td-text-color-secondary)] flex items-center gap-1.5 mb-1 uppercase tracking-wider">
            <time-icon size="14px" /> 最近 5 次运行时间预测
          </div>

          <div v-if="cronError" class="text-sm font-medium text-red-500 bg-red-50 dark:bg-red-950/30 p-3 rounded-lg border border-red-100 dark:border-red-900/50">
            {{ cronError }}
          </div>

          <ul v-else class="grid grid-cols-1 sm:grid-cols-2 gap-2 m-0 p-0 list-none">
            <li
              v-for="(item, index) in nextExecutions"
              :key="index"
              class="text-sm font-mono font-medium text-zinc-700 dark:text-zinc-300 bg-zinc-50 dark:bg-zinc-800/40 !px-4 !py-3 rounded-lg border border-zinc-100 dark:border-zinc-700/50 flex items-center justify-between transition-colors hover:border-[var(--color-primary)]/30 hover:bg-[var(--color-primary)]/5"
            >
              <div class="flex items-center gap-2">
                <span class="text-[var(--td-text-color-secondary)] w-6 font-bold opacity-70">#{{ index + 1 }}</span>
                <span>{{ item.formattedTime }}</span>
              </div>
              <span class="text-xs px-2 py-0.5 rounded font-semibold bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400">
                {{ item.weekday }}
              </span>
            </li>
          </ul>
        </div>

      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

.custom-scrollbar {
  .scrollbar-mixin();
}

.custom-tabs {
  :deep(.t-tabs__nav-container) {
    background-color: transparent !important;
    border-bottom: 1px solid var(--td-component-stroke) !important;
    padding: 0 12px;
  }
  :deep(.t-tabs__content),
  :deep(.t-tab-panel) {
    background-color: transparent !important;
    padding: 0 !important;
  }
  :deep(.t-tabs__nav-item) {
    background-color: transparent !important;
    color: var(--td-text-color-secondary);
    font-size: 13px;
    &:hover {
      color: var(--td-text-color-primary);
    }
    &.t-is-active {
      color: var(--td-brand-color);
      font-weight: bold;
    }
  }
}
</style>
