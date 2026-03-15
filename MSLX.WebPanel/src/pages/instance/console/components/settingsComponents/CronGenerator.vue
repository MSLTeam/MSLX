<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { CheckCircleIcon, ErrorCircleIcon, TimeIcon } from 'tdesign-icons-vue-next';
import { CronExpressionParser } from 'cron-parser';

const props = defineProps({
  visible: { type: Boolean, default: false },
  initialValue: { type: String, default: '' },
});

const emits = defineEmits(['update:visible', 'confirm']);

// --- 模式管理 ---
const mode = ref<'preset' | 'novice' | 'pro'>('preset');

// --- 预设模式数据 ---
const selectedPreset = ref('*/10 * * * * *'); // 默认选中第一条
const presetOptions = [
  { label: '每 10 秒', value: '*/10 * * * * *' },
  { label: '每分钟', value: '0 * * * * *' },
  { label: '每 5 分钟', value: '0 */5 * * * *' },
  { label: '每 30 分钟', value: '0 */30 * * * *' },
  { label: '每小时', value: '0 0 * * * ?' },
  { label: '每天凌晨 3 点', value: '0 0 3 * * ?' },
  { label: '每周日凌晨 3 点', value: '0 0 3 ? * 1' },
];

const applyPreset = (val: string) => {
  selectedPreset.value = val;
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
  { label: '周', value: 'week', min: 1, max: 7 },
];
const state = ref<Record<string, any>>({
  second: { type: 'every', start: 0, step: 1, specifics: [] },
  minute: { type: 'every', start: 0, step: 1, specifics: [] },
  hour: { type: 'every', start: 0, step: 1, specifics: [] },
  day: { type: 'every', start: 1, step: 1, specifics: [] },
  month: { type: 'every', start: 1, step: 1, specifics: [] },
  week: { type: 'any', start: 1, step: 1, specifics: [] },
});
const weekOptions = [
  { label: '周日', value: 1 },
  { label: '周一', value: 2 },
  { label: '周二', value: 3 },
  { label: '周三', value: 4 },
  { label: '周四', value: 5 },
  { label: '周五', value: 6 },
  { label: '周六', value: 7 },
];

// --- 计算逻辑 ---
const proCron = computed(() => {
  const getVal = (unit: string, defaultVal = '*') => {
    const s = state.value[unit];
    if (unit === 'week' && s.type === 'any') return '?';
    if (unit === 'week' && state.value['day'].type !== 'every') return '?';
    if (unit === 'day' && state.value['week'].type !== 'any') return '?';

    if (s.type === 'every') return '*';
    if (s.type === 'interval') return `${s.start}/${s.step}`;
    if (s.type === 'specific') {
      if (s.specifics.length === 0) return unit === 'day' || unit === 'month' ? '1' : '0';
      return s.specifics.sort((a: number, b: number) => a - b).join(',');
    }
    return defaultVal;
  };

  const s = getVal('second');
  const m = getVal('minute');
  const h = getVal('hour');
  const d = getVal('day');
  const M = getVal('month');
  const w = getVal('week', '?');
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

// 最终表达式根据当前处于哪个 Tab 模式来决定
const finalCron = computed(() => {
  if (mode.value === 'preset') return selectedPreset.value;
  if (mode.value === 'novice') return noviceCron.value;
  return proCron.value;
});

// --- 下5次执行时间计算 ---
const nextExecutions = ref<string[]>([]);
const cronError = ref('');

const calculateNextRuns = () => {
  try {
    const interval = CronExpressionParser.parse(finalCron.value, {
      currentDate: new Date(),
    });

    nextExecutions.value = interval.take(5).map((d: any) => {
      return new Date(d.toString()).toLocaleString();
    });
    cronError.value = '';
  } catch {
    // 解析失败
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

// --- 交互逻辑 ---
watch(
  () => props.visible,
  (val) => {
    // 弹窗打开时，如果有外界传进来的初始值，直接切到专业模式并验证
    if (val && props.initialValue) {
      mode.value = 'pro';
      calculateNextRuns();
    } else if (val && !props.initialValue) {
      // 如果没有初始值，默认进入预设模式
      mode.value = 'preset';
    }
  },
);

const handleConfirm = () => {
  if (cronError.value) {
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
        <t-radio-group v-model="mode" variant="default-filled" class="!bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-lg p-0.5 shadow-sm">
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

              <t-radio-group v-model="state[unit.value].type" direction="vertical" class="w-full gap-4 !bg-transparent">
                <t-radio value="every" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  {{ unit.value === 'week' ? '不指定 (?)' : `每${unit.label} (*)` }}
                </t-radio>

                <t-radio v-if="unit.value !== 'week'" value="interval" class="text-sm font-medium text-zinc-700 dark:text-zinc-300">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span>周期: 从</span>
                    <t-input-number v-model="state[unit.value].start" :min="unit.min" :max="unit.max" size="small" theme="column" class="!w-[70px]" />
                    <span>{{ unit.label }} 开始，每</span>
                    <t-input-number v-model="state[unit.value].step" :min="1" :max="unit.max" size="small" theme="column" class="!w-[70px]" />
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
                />
              </div>

            </div>
          </t-tab-panel>
        </t-tabs>
      </div>

      <div class="bg-[var(--td-bg-color-container)]/80 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm p-5 flex flex-col gap-4 backdrop-blur-md">

        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 border-b border-dashed border-zinc-200 dark:border-zinc-700/60">
          <span class="font-bold text-[var(--td-text-color-primary)] text-sm">当前表达式:</span>
          <div class="flex items-center gap-3 bg-zinc-50 dark:bg-zinc-900 px-3 py-1.5 rounded-lg border border-zinc-100 dark:border-zinc-800 shadow-inner">
            <span class="font-mono text-lg font-bold tracking-wider text-[var(--color-primary)]">{{ finalCron }}</span>
            <t-tag v-if="!cronError" theme="success" variant="light" size="small" class="!rounded"><template #icon><check-circle-icon /></template> 有效</t-tag>
            <t-tag v-else theme="danger" variant="light" size="small" class="!rounded"><template #icon><error-circle-icon /></template> 无效</t-tag>
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
            <li v-for="(time, index) in nextExecutions" :key="index" class="text-sm font-mono font-medium text-zinc-700 dark:text-zinc-300 bg-zinc-50 dark:bg-zinc-800/40 !px-4 !py-3 rounded-lg border border-zinc-100 dark:border-zinc-700/50 flex items-center transition-colors hover:border-[var(--color-primary)]/30 hover:bg-[var(--color-primary)]/5">
              <span class="text-[var(--td-text-color-secondary)] w-6 font-bold opacity-70">#{{ index + 1 }}</span>
              {{ time }}
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
