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
const mode = ref<'novice' | 'pro'>('novice');

// --- 新手模式数据 ---
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

const finalCron = computed(() => {
  return mode.value === 'novice' ? noviceCron.value : proCron.value;
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
    if (val && props.initialValue) {
      mode.value = 'pro';
      calculateNextRuns();
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
    @close="emits('update:visible', false)"
    @confirm="handleConfirm"
  >
    <div class="cron-gen-container">
      <div class="mode-switch">
        <t-radio-group v-model="mode" variant="default-filled">
          <t-radio-button value="novice">🚀 新手模式</t-radio-button>
          <t-radio-button value="pro">🛠️ 专业模式</t-radio-button>
        </t-radio-group>
      </div>

      <div v-if="mode === 'novice'" class="mode-content novice-panel">
        <div class="novice-input-group">
          <span>每隔</span>
          <t-input-number v-model="noviceValue" :min="1" theme="column" style="width: 100px" />
          <t-select v-model="noviceUnit" :options="noviceOptions" style="width: 100px" />
          <span>执行一次</span>
        </div>
        <t-alert theme="info" style="margin-top: 16px">
          <template #message>
            此模式适用于简单的周期性任务。如果需要“每周五上午10点”等复杂规则，请切换到<b>专业模式</b>。
          </template>
        </t-alert>
      </div>

      <div v-else class="mode-content pro-panel">
        <t-tabs v-model="activeTab">
          <t-tab-panel v-for="unit in timeUnits" :key="unit.value" :value="unit.value" :label="unit.label">
            <div class="tab-scroll-area">
              <t-radio-group v-model="state[unit.value].type" direction="vertical">
                <t-radio value="every">{{ unit.value === 'week' ? '不指定 (?)' : `每${unit.label} (*)` }}</t-radio>
                <t-radio v-if="unit.value !== 'week'" value="interval">
                  周期: 从
                  <t-input-number
                    v-model="state[unit.value].start"
                    :min="unit.min"
                    :max="unit.max"
                    size="small"
                    theme="column"
                    style="width: 60px"
                  />
                  {{ unit.label }}开始，每
                  <t-input-number
                    v-model="state[unit.value].step"
                    :min="1"
                    :max="unit.max"
                    size="small"
                    theme="column"
                    style="width: 60px"
                  />
                  {{ unit.label }}执行一次
                </t-radio>
                <t-radio v-if="unit.value !== 'week'" value="specific">指定: 选择具体的{{ unit.label }}</t-radio>
                <t-radio v-if="unit.value === 'week'" value="specific">指定周几</t-radio>
              </t-radio-group>

              <div v-if="state[unit.value].type === 'specific'" class="specific-box">
                <t-checkbox-group
                  v-model="state[unit.value].specifics"
                  :options="unit.value === 'week' ? weekOptions : getSpecificOptions(unit.value)"
                  class="grid-checkbox"
                />
              </div>
            </div>
          </t-tab-panel>
        </t-tabs>
      </div>

      <div class="result-area">
        <div class="cron-display">
          <span class="label">当前表达式:</span>
          <div class="code-box">
            {{ finalCron }}
            <t-tag v-if="!cronError" theme="success" variant="light" size="small"><check-circle-icon /> 有效</t-tag>
            <t-tag v-else theme="danger" variant="light" size="small"><error-circle-icon /> 无效</t-tag>
          </div>
        </div>

        <div class="next-runs">
          <div class="runs-title"><time-icon /> 最近 5 次运行时间预测:</div>
          <div v-if="cronError" class="error-msg">{{ cronError }}</div>
          <ul v-else class="runs-list">
            <li v-for="(time, index) in nextExecutions" :key="index">
              <span class="index">#{{ index + 1 }}</span> {{ time }}
            </li>
          </ul>
        </div>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.cron-gen-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.mode-switch {
  display: flex;
  justify-content: center;
  margin-bottom: 8px;
}

.mode-content {
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  padding: 16px;
  background-color: var(--td-bg-color-container);
  min-height: 250px;
}

.novice-panel {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  .novice-input-group {
    display: flex;
    align-items: center;
    gap: 12px;
    font-size: 16px;
    font-weight: 500;
  }
}

.pro-panel {
  padding: 0;
  border: none;
  .tab-scroll-area {
    padding: 16px 0;
    max-height: 220px;
    overflow-y: auto;
  }
  .specific-box {
    margin-top: 12px;
    padding: 12px;
    background: var(--td-bg-color-secondarycontainer);
    border-radius: var(--td-radius-medium);
  }
  .grid-checkbox {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(50px, 1fr));
    gap: 8px;
  }
}

.result-area {
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;

  .cron-display {
    display: flex;
    align-items: center;
    gap: 12px;
    padding-bottom: 12px;
    border-bottom: 1px dashed var(--td-component-stroke);
    .label {
      font-weight: bold;
      color: var(--td-text-color-primary);
    }
    .code-box {
      font-family: 'Consolas', monospace;
      font-size: 18px;
      color: var(--td-brand-color);
      display: flex;
      align-items: center;
      gap: 12px;
    }
  }

  .next-runs {
    .runs-title {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      margin-bottom: 8px;
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .error-msg {
      color: var(--td-error-color);
      font-size: 12px;
    }
    .runs-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 4px;
      li {
        font-size: 12px;
        color: var(--td-text-color-primary);
        background: var(--td-bg-color-container);
        padding: 4px 8px;
        border-radius: 4px;
        .index {
          color: var(--td-text-color-placeholder);
          margin-right: 6px;
        }
      }
    }
  }
}
</style>
