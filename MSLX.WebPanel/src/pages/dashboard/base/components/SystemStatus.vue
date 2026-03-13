<script setup lang="ts">
import { ref, onMounted, onUnmounted, reactive, nextTick } from 'vue';
import { Tag as TTag } from 'tdesign-vue-next';
import * as signalR from '@microsoft/signalr';
import * as echarts from 'echarts';
import { useUserStore } from '@/store/modules/user';

// --- 类型定义 ---
interface SystemStats {
  timestamp: string;
  cpu: number;
  memTotal: number;
  memUsed: number;
  memUsage: number;
}

// --- 状态数据 ---
const userStore = useUserStore();
const connection = ref<signalR.HubConnection | null>(null);
const isConnected = ref(false);

const cpuChartRef = ref<HTMLElement | null>(null);
const memChartRef = ref<HTMLElement | null>(null);

let cpuChartInst: echarts.ECharts | null = null;
let memChartInst: echarts.ECharts | null = null;

const currentStats = reactive({
  cpu: 0,
  memUsage: 0,
  memUsed: 0,
  memTotal: 0,
});

const maxPoints = 30;
const timeData: string[] = [];
const cpuData: number[] = [];
const memData: number[] = [];

// 获取真实颜色
const getCssVar = (name: string): string => {
  const val = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return val || '#999999';
};

// --- ECharts 配置 ---
const getChartOption = (color: string, name: string, data: number[]) => {
  return {
    grid: {
      top: 10,
      right: 10,
      bottom: 0,
      left: 0,
      containLabel: false,
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'var(--td-bg-color-container)',
      borderColor: 'var(--td-component-border)',
      textStyle: { color: 'var(--td-text-color-primary)' },
      formatter: (params: any) => {
        const item = params[0];
        return `${item.name}<br/><span style="color:${color}">●</span> ${name}: <b>${item.value}%</b>`;
      },
    },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: timeData,
      show: false,
    },
    yAxis: {
      type: 'value',
      max: 100,
      min: 0,
      show: false,
    },
    series: [
      {
        name: name,
        type: 'line',
        smooth: true,
        showSymbol: false,
        lineStyle: {
          width: 2,
          color: color,
        },
        areaStyle: {
          opacity: 0.2,
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: color },
            { offset: 1, color: 'transparent' },
          ]),
        },
        data: data,
      },
    ],
  };
};

const initCharts = () => {
  const brandColor = getCssVar('--td-brand-color');
  const successColor = getCssVar('--td-success-color');

  if (cpuChartRef.value) {
    cpuChartInst = echarts.init(cpuChartRef.value);
    cpuChartInst.setOption(getChartOption(brandColor, 'CPU', []));
  }
  if (memChartRef.value) {
    memChartInst = echarts.init(memChartRef.value);
    memChartInst.setOption(getChartOption(successColor, 'Memory', []));
  }

  window.addEventListener('resize', handleResize);
};

const handleResize = () => {
  cpuChartInst?.resize();
  memChartInst?.resize();
};

// --- SignalR 逻辑 ---
const initSignalR = async () => {
  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/system', baseUrl || window.location.origin);

  if (token) hubUrl.searchParams.append('x-user-token', token);

  connection.value = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .withAutomaticReconnect()
    .build();

  connection.value.on('ReceiveSystemStats', (data: SystemStats) => {
    currentStats.cpu = data.cpu;
    currentStats.memUsage = data.memUsage;
    currentStats.memUsed = data.memUsed;
    currentStats.memTotal = data.memTotal;

    if (timeData.length >= maxPoints) {
      timeData.shift();
      cpuData.shift();
      memData.shift();
    }
    timeData.push(data.timestamp);
    cpuData.push(data.cpu);
    memData.push(data.memUsage);

    cpuChartInst?.setOption({ xAxis: { data: timeData }, series: [{ data: cpuData }] });
    memChartInst?.setOption({ xAxis: { data: timeData }, series: [{ data: memData }] });
  });

  try {
    await connection.value.start();
    isConnected.value = true;
    await connection.value.invoke('JoinMonitor');
  } catch (err) {
    console.error('SignalR 连接失败:', err);
  }

  connection.value.onclose(() => {
    isConnected.value = false;
  });
};

onMounted(() => {
  nextTick(() => {
    setTimeout(() => {
      initCharts();
      initSignalR();
    }, 100);
  });
});

onUnmounted(async () => {
  window.removeEventListener('resize', handleResize);
  if (connection.value) {
    try {
      await connection.value.invoke('LeaveMonitor');
      await connection.value.stop();
    } catch (e) {
      console.error(e);
    }
  }
  cpuChartInst?.dispose();
  memChartInst?.dispose();
});
</script>

<template>
  <div class="w-full">
    <div class="grid grid-cols-1 md:grid-cols-2 gap-4">

      <div class="design-card w-full bg-white dark:bg-zinc-800 p-4 sm:p-5 rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">
        <div class="flex justify-between items-center h-[100px] w-full gap-2">

          <div class="flex flex-col justify-center min-w-[100px] shrink-0">
            <div class="text-[13px] text-[var(--td-text-color-secondary)] font-medium">CPU 使用率</div>
            <div class="flex items-baseline my-1">
              <span class="text-3xl font-bold font-mono text-[var(--td-text-color-primary)] leading-none">{{ currentStats.cpu }}</span>
              <span class="text-xs ml-0.5 text-[var(--td-text-color-secondary)]">%</span>
            </div>

            <t-tag v-if="isConnected" theme="success" shape="round" size="small" class="w-fit mt-1">
              <template #icon>
                <span class="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 animate-pulse mr-1"></span>
              </template>
              实时
            </t-tag>
            <t-tag v-else theme="danger" shape="round" size="small" class="w-fit mt-1">
              <template #icon>
                <span class="w-1.5 h-1.5 rounded-full bg-red-500 dark:bg-red-400 mr-1"></span>
              </template>
              离线
            </t-tag>
          </div>

          <div ref="cpuChartRef" class="flex-1 h-full min-w-[120px] overflow-hidden"></div>
        </div>
      </div>

      <div class="design-card w-full bg-white dark:bg-zinc-800 p-4 sm:p-5 rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">
        <div class="flex justify-between items-center h-[100px] w-full gap-2">

          <div class="flex flex-col justify-center min-w-[100px] shrink-0">
            <div class="text-[13px] text-[var(--td-text-color-secondary)] font-medium">内存使用率</div>
            <div class="flex items-baseline my-1">
              <span class="text-3xl font-bold font-mono text-[var(--td-text-color-primary)] leading-none">{{ currentStats.memUsage }}</span>
              <span class="text-xs ml-0.5 text-[var(--td-text-color-secondary)]">%</span>
            </div>

            <div class="mt-1 flex items-baseline gap-1 font-mono">
              <span class="text-[14px] font-bold text-zinc-700 dark:text-zinc-200">{{ (currentStats.memUsed / 1024).toFixed(1) }}</span>
              <span class="text-[11px] text-[var(--td-text-color-secondary)] mx-0.5">/</span>
              <span class="text-[11px] text-[var(--td-text-color-secondary)]">{{ (currentStats.memTotal / 1024).toFixed(1) }} GB</span>
            </div>

          </div>

          <div ref="memChartRef" class="flex-1 h-full min-w-[120px] overflow-hidden"></div>
        </div>
      </div>

    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
