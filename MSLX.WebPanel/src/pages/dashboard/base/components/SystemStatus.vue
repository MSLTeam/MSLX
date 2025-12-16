<script setup lang="ts">
import { ref, onMounted, onUnmounted, reactive, nextTick } from 'vue';
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
      top: 10,    // 调小边距
      right: 10,
      bottom: 0,  // 贴底
      left: 0,    // 贴左
      containLabel: false, // 不包含坐标轴标签
    },
    tooltip: {
      trigger: 'axis',
      backgroundColor: 'var(--td-bg-color-container)', // Tooltip 是 DOM，可以用 var
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
      show: false, // 隐藏坐标轴，更简洁
    },
    yAxis: {
      type: 'value',
      max: 100,
      min: 0,
      show: false, // 隐藏坐标轴
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
            { offset: 1, color: 'transparent' }, // 透明
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

  // 建连
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
    // 延迟一点点
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
  <div class="monitor-dashboard">
    <t-row :gutter="[12, 12]">
      <t-col :xs="12" :md="6">
        <t-card shadow :bordered="false" class="chart-card" size="small">
          <div class="card-content">
            <div class="info-section">
              <div class="label">CPU 使用率</div>
              <div class="value-group">
                <span class="value">{{ currentStats.cpu }}</span>
                <span class="unit">%</span>
              </div>
              <t-tag v-if="isConnected" theme="success" variant="light" size="small" class="status-tag">
                实时
              </t-tag>
              <t-tag v-else theme="danger" variant="light" size="small" class="status-tag">
                离线
              </t-tag>
            </div>
            <div ref="cpuChartRef" class="chart-container"></div>
          </div>
        </t-card>
      </t-col>

      <t-col :xs="12" :md="6">
        <t-card shadow :bordered="false" class="chart-card" size="small">
          <div class="card-content">
            <div class="info-section">
              <div class="label">内存使用率</div>
              <div class="value-group">
                <span class="value">{{ currentStats.memUsage }}</span>
                <span class="unit">%</span>
              </div>
              <div class="sub-text">
                {{ (currentStats.memUsed / 1024).toFixed(1) }} / {{ (currentStats.memTotal / 1024).toFixed(1) }} GB
              </div>
            </div>
            <div ref="memChartRef" class="chart-container"></div>
          </div>
        </t-card>
      </t-col>
    </t-row>
  </div>
</template>

<style scoped lang="less">
.monitor-dashboard {
  width: 100%;
}

.chart-card {
  transition: all 0.3s;
  border-radius: 6px;
  background-color: var(--td-bg-color-container);
}

.card-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  height: 100px;
  padding: 0 4px;
}

.info-section {
  display: flex;
  flex-direction: column;
  justify-content: center;
  min-width: 100px;

  .label {
    font-size: 13px;
    color: var(--td-text-color-secondary);
  }

  .value-group {
    display: flex;
    align-items: baseline;
    margin: 4px 0;

    .value {
      font-size: 28px;
      font-weight: 700;
      line-height: 1;
      font-family: 'DIN Alternate', monospace;
      color: var(--td-text-color-primary);
    }
    .unit {
      font-size: 12px;
      margin-left: 2px;
      color: var(--td-text-color-secondary);
    }
  }

  .sub-text {
    font-size: 12px;
    color: var(--td-text-color-placeholder);
  }

  .status-tag {
    width: fit-content;
    margin-top: 4px;
  }
}

.chart-container {
  flex: 1; // 图表占据剩余空间
  height: 100%; // 占满父容器高度
  min-width: 120px;
  overflow: hidden;
}
</style>
