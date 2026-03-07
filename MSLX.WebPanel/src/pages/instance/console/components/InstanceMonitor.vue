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
  <div class="flex flex-col gap-3">

    <div class="flex justify-between items-center text-xs text-zinc-500 dark:text-zinc-400 pb-2 border-b border-dashed border-zinc-200 dark:border-zinc-700/60">
      <span class="flex items-center gap-1.5">
        <wifi-icon :class="hubStore.isConnected ? 'text-emerald-500' : 'text-zinc-400 dark:text-zinc-600'" />
        {{ hubStore.isConnected ? '连接正常' : '正在连接数据流...' }}
      </span>
      <div
        class="w-1.5 h-1.5 rounded-full transition-all duration-300"
        :class="hubStore.isConnected && isRunning ? 'bg-emerald-500 shadow-[0_0_6px_rgba(16,185,129,0.8)]' : 'bg-zinc-300 dark:bg-zinc-600'"
      ></div>
    </div>

    <div class="flex flex-col gap-4">

      <div class="flex flex-col gap-2">
        <div class="flex justify-between items-center">
          <div class="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400">
            <dashboard-icon size="14px" /> CPU 使用率
          </div>
          <div class="font-mono font-medium text-xs text-zinc-800 dark:text-zinc-200">
            {{ hubStore.stats.cpu.toFixed(1) }} %
          </div>
        </div>
        <div ref="cpuChartRef" class="h-[50px] w-full overflow-hidden rounded-md bg-transparent border border-zinc-200/50 dark:border-zinc-700/50"></div>
      </div>

      <div class="flex flex-col gap-2">
        <div class="flex justify-between items-center">
          <div class="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400">
            <chart-line-data-icon size="14px" /> 内存使用
          </div>
          <div class="font-mono font-medium text-xs text-zinc-800 dark:text-zinc-200 flex items-center gap-1">
            {{ formattedMemory }}
            <span v-if="hasMaxMemory" class="text-[11px] text-zinc-400 dark:text-zinc-500">
              ({{ hubStore.stats.memPercent.toFixed(1) }}%)
            </span>
          </div>
        </div>
        <div ref="memChartRef" class="h-[50px] w-full overflow-hidden rounded-md bg-transparent border border-zinc-200/50 dark:border-zinc-700/50"></div>
      </div>

    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
