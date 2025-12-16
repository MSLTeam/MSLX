<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick, watch, computed } from 'vue';
import * as echarts from 'echarts';
import { DashboardIcon, ChartLineDataIcon, WifiIcon } from 'tdesign-icons-vue-next'; // 引入 WifiIcon 作为连接状态示意
import { useInstanceHubStore } from '@/store/modules/instanceHub';

const props = defineProps<{
  serverId: number;
  maxMemory: number;
  isRunning: boolean;
}>();

const hubStore = useInstanceHubStore();
const cpuChartRef = ref<HTMLElement | null>(null);
const memChartRef = ref<HTMLElement | null>(null);
let cpuChartInst: echarts.ECharts | null = null;
let memChartInst: echarts.ECharts | null = null;

const maxPoints = 20;
const cpuData = ref<number[]>(new Array(maxPoints).fill(0));
const memData = ref<number[]>(new Array(maxPoints).fill(0));

const hasMaxMemory = computed(() => props.maxMemory > 0);

// --- ECharts 逻辑保持不变 ---
const getSparklineOption = (color: string, data: number[], yMax: number | null = 100) => {
  return {
    grid: { top: 4, right: 0, bottom: 4, left: 0 }, // 微调边距
    xAxis: { type: 'category', show: false, boundaryGap: false },
    yAxis: {
      type: 'value',
      min: 0,
      max: yMax === null ? undefined : yMax,
      show: false,
    },
    series: [
      {
        type: 'line',
        data: data,
        smooth: true,
        showSymbol: false,
        lineStyle: { width: 2, color: color }, // 线条稍微加粗一点点
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: color.replace(')', ', 0.2)').replace('rgb', 'rgba') },
            { offset: 1, color: 'transparent' },
          ]),
        },
      },
    ],
  };
};

const initCharts = () => {
  const style = getComputedStyle(document.documentElement);
  const brandColor = style.getPropertyValue('--td-brand-color').trim() || '#0052d9';
  const warningColor = style.getPropertyValue('--td-warning-color').trim() || '#e37318';

  if (cpuChartRef.value) {
    cpuChartInst = echarts.init(cpuChartRef.value);
    cpuChartInst.setOption(getSparklineOption(brandColor, cpuData.value, 100));
  }
  if (memChartRef.value) {
    memChartInst = echarts.init(memChartRef.value);
    const yMax = hasMaxMemory.value ? 100 : null;
    memChartInst.setOption(getSparklineOption(warningColor, memData.value, yMax));
  }
  window.addEventListener('resize', handleResize);
};

const handleResize = () => {
  cpuChartInst?.resize();
  memChartInst?.resize();
};

const formattedMemory = computed(() => {
  const mb = hubStore.stats.memBytes / 1024 / 1024;
  if (mb > 1024) return `${(mb / 1024).toFixed(2)} GB`;
  return `${mb.toFixed(0)} MB`;
});

const connectStore = async () => {
  if (props.isRunning && props.serverId) {
    hubStore.setMaxMemory(props.maxMemory);
    await hubStore.connect(props.serverId);
  }
};

const disconnectStore = async () => {
  await hubStore.disconnect();
};

watch(
  () => props.maxMemory,
  (val) => {
    hubStore.setMaxMemory(val);
    if (memChartInst) {
      const yMax = val > 0 ? 100 : null;
      memChartInst.setOption({ yAxis: { max: yMax === null ? undefined : yMax } });
    }
  },
  { immediate: true }
);

watch(
  () => hubStore.stats,
  (newStats) => {
    if (!props.isRunning || hubStore.currentServerId !== props.serverId) return;

    cpuData.value.push(newStats.cpu);
    if (cpuData.value.length > maxPoints) cpuData.value.shift();

    if (hasMaxMemory.value) {
      memData.value.push(newStats.memPercent);
    } else {
      const valMB = newStats.memBytes / 1024 / 1024;
      memData.value.push(valMB);
    }
    if (memData.value.length > maxPoints) memData.value.shift();

    cpuChartInst?.setOption({ series: [{ data: cpuData.value }] });
    memChartInst?.setOption({ series: [{ data: memData.value }] });
  },
  { deep: true }
);

watch(() => props.isRunning, async (val) => {
  if (val) {
    nextTick(() => {
      handleResize();
      connectStore();
    });
  } else {
    cpuData.value.fill(0);
    memData.value.fill(0);
    cpuChartInst?.setOption({ series: [{ data: cpuData.value }] });
    memChartInst?.setOption({ series: [{ data: memData.value }] });
    await disconnectStore();
  }
});

watch(() => props.serverId, async (newVal, oldVal) => {
  if (newVal !== oldVal) {
    await disconnectStore();
    if (props.isRunning) await connectStore();
  }
});

onMounted(async () => {
  await nextTick();
  initCharts();
  if (props.isRunning) {
    await connectStore();
  }
});

onUnmounted(async () => {
  window.removeEventListener('resize', handleResize);
  await disconnectStore();
  cpuChartInst?.dispose();
  memChartInst?.dispose();
});
</script>

<template>
  <div class="monitor-container">
    <div class="status-header">
      <span class="status-text">
        <wifi-icon v-if="hubStore.isConnected" style="color: var(--td-success-color)" />
        <wifi-icon v-else style="color: var(--td-text-color-disabled)" />
        {{ hubStore.isConnected ? '连接正常' : '正在连接数据流...' }}
      </span>
      <div class="monitor-dot" :class="{ active: hubStore.isConnected && isRunning }"></div>
    </div>

    <div class="monitor-content">
      <div class="monitor-block">
        <div class="info-item">
          <div class="label"><dashboard-icon /> CPU 使用率</div>
          <div class="value">{{ hubStore.stats.cpu.toFixed(1) }} %</div>
        </div>
        <div ref="cpuChartRef" class="chart-wrapper"></div>
      </div>

      <div class="divider"></div>

      <div class="monitor-block">
        <div class="info-item">
          <div class="label"><chart-line-data-icon /> 内存使用</div>
          <div class="value">
            {{ formattedMemory }}
            <span v-if="hasMaxMemory" class="sub-val">
              ({{ hubStore.stats.memPercent.toFixed(1) }}%)
            </span>
          </div>
        </div>
        <div ref="memChartRef" class="chart-wrapper"></div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
.monitor-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.status-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
  color: var(--td-text-color-placeholder);
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);

  .status-text {
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .monitor-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--td-bg-color-component-disabled);
    transition: all 0.3s;

    &.active {
      background: var(--td-success-color);
      box-shadow: 0 0 6px var(--td-success-color);
    }
  }
}

.monitor-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.monitor-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;

  .label {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--td-text-color-secondary);
    font-size: 13px;
  }

  .value {
    font-family: var(--td-font-family-number);
    font-weight: 500;
    font-size: 13px;
    display: flex;
    align-items: center;
    gap: 4px;

    .sub-val {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
    }
  }
}

.chart-wrapper {
  height: 50px;
  width: 100%;
  overflow: hidden;
  border-radius: 6px;
  background: var(--td-bg-color-secondary-container);
  border: 1px solid var(--td-component-stroke);
}

.divider {
  display: none;
  height: 1px;
  background: var(--td-component-stroke);
  opacity: 0.5;
}
</style>
