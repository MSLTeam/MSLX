<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted, nextTick } from 'vue';
import * as echarts from 'echarts';
import { useInstanceHubStore } from '@/store/modules/instanceHub';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  getOnlinePlayers,
  getHistoryPlayers,
  getWhitelist,
  addWhitelist,
  removeWhitelist,
  getOps,
  addOp,
  removeOp,
  getBannedPlayers,
  addBannedPlayer,
  removeBannedPlayer,
  getBannedIps,
  addBannedIp,
  removeBannedIp,
} from '@/api/instance';
import {
  UserIcon,
  UserClearIcon,
  SecuredIcon,
  CloseCircleIcon,
  UsergroupIcon,
  AddIcon,
  DeleteIcon,
  TimeIcon,
  RefreshIcon,
  SearchIcon,
  ChartLineDataIcon,
} from 'tdesign-icons-vue-next';
import { useRoute } from 'vue-router';
import type {
  BannedIpItem,
  BannedPlayerItem,
  OpItem,
  UserCacheItem,
  WhitelistItem,
  DailyActiveStat,
} from '@/api/model/instance';

const route = useRoute();
const props = defineProps<{
  visible: boolean;
  serverId: number;
  isRunning: boolean;
}>();

const emits = defineEmits<{
  'update:visible': [value: boolean];
}>();

const hubStore = useInstanceHubStore();
const activeTab = ref('history'); // 默认显示“玩家数据”
const banType = ref('player');
const loading = ref(false);

// 操作模式（指令/修改配置）
const opMode = ref('command');

// 数据源
const onlinePlayers = ref<string[]>([]);
const historyPlayers = ref<UserCacheItem[]>([]);
const activityChartData = ref<DailyActiveStat[]>([]);
const rangeStats = ref<Record<string, DailyActiveStat[]>>({});
const activeRange = ref('1d');
const rangeOptions = [
  { label: '30天', value: '30d' },
  { label: '14天', value: '14d' },
  { label: '7天', value: '7d' },
  { label: '1天', value: '1d' },
  { label: '6小时', value: '6h' },
];

const searchHistory = ref('');
const whitelist = ref<WhitelistItem[]>([]);
const ops = ref<OpItem[]>([]);
const bannedPlayers = ref<BannedPlayerItem[]>([]);
const bannedIps = ref<BannedIpItem[]>([]);

// 输入框
const inputNewWhitelist = ref('');
const inputNewOp = ref('');
const inputNewBanPlayer = ref('');
const inputNewBanReason = ref('');
const inputNewBanIp = ref('');

// 图表引用与实例
const chartRef = ref<HTMLElement | null>(null);
let chartInst: echarts.ECharts | null = null;

// 当前选定粒度的图表数据
const currentChartData = computed(() => {
  if (rangeStats.value && rangeStats.value[activeRange.value]) {
    return rangeStats.value[activeRange.value];
  }
  return activityChartData.value;
});

// 当前粒度的标题描述
const rangeTitle = computed(() => {
  switch (activeRange.value) {
    case '30d':
      return '近 30 天活跃趋势 (DAU)';
    case '14d':
      return '近 14 天活跃趋势 (DAU)';
    case '7d':
      return '近 7 天活跃趋势 (DAU)';
    case '1d':
      return '近 24 小时活跃趋势';
    case '6h':
      return '近 6 小时活跃趋势';
    default:
      return '活跃人数趋势';
  }
});

// 过滤后的玩家历史列表
const filteredHistoryPlayers = computed(() => {
  if (!searchHistory.value.trim()) return historyPlayers.value;
  const kw = searchHistory.value.trim().toLowerCase();
  return historyPlayers.value.filter(
    (p) => p.name.toLowerCase().includes(kw) || (p.uuid && p.uuid.toLowerCase().includes(kw)),
  );
});

// 统计指标
const todayActiveCount = computed(() => {
  if (activityChartData.value.length === 0) return 0;
  return activityChartData.value[activityChartData.value.length - 1]?.count || 0;
});

// 初始化或更新活跃人数趋势图
const updateChart = () => {
  const data = currentChartData.value;
  if (!chartRef.value || data.length === 0) return;
  if (!chartInst) {
    chartInst = echarts.init(chartRef.value);
  }

  const style = getComputedStyle(document.documentElement);
  const brandColor = style.getPropertyValue('--td-brand-color').trim() || '#0052d9';
  const textColor = style.getPropertyValue('--td-text-color-secondary').trim() || '#909399';
  const borderColor = style.getPropertyValue('--td-component-border').trim() || 'rgba(0, 0, 0, 0.08)';
  const isHourly = activeRange.value === '1d' || activeRange.value === '6h';

  chartInst.setOption({
    tooltip: {
      trigger: 'axis',
      axisPointer: {
        type: 'line',
        lineStyle: { color: brandColor, type: 'dashed' },
      },
      formatter: (params: any) => {
        const item = params[0];
        const labelPrefix = isHourly ? '时间' : '日期';
        return `<div style="font-size:12px; line-height: 1.5;">
          <div style="color:#909399">${labelPrefix}: ${item.axisValue}</div>
          <div style="font-weight:600; margin-top:2px;">活跃人数: <span style="color:${brandColor}">${item.value}</span> 人</div>
        </div>`;
      },
      backgroundColor: 'var(--td-bg-color-container)',
      borderColor,
      textStyle: { color: 'var(--td-text-color-primary)' },
      padding: [6, 10],
      extraCssText: 'box-shadow: 0 4px 12px rgba(0,0,0,0.1); border-radius: 8px;',
    },
    grid: { top: 12, right: 12, bottom: 20, left: 24 },
    xAxis: {
      type: 'category',
      data: data.map((d) => d.date),
      boundaryGap: false,
      axisLine: { show: false },
      axisTick: { show: false },
      axisLabel: { color: textColor, fontSize: 10 },
    },
    yAxis: {
      type: 'value',
      minInterval: 1,
      splitLine: {
        lineStyle: {
          color: borderColor,
          type: 'dashed',
        },
      },
      axisLabel: { color: textColor, fontSize: 10 },
    },
    series: [
      {
        name: '活跃人数',
        type: 'line',
        data: data.map((d) => d.count),
        smooth: true,
        showSymbol: true,
        symbolSize: 4,
        itemStyle: { color: brandColor },
        lineStyle: { width: 2, color: brandColor },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: 'rgba(0, 82, 217, 0.25)' },
            { offset: 1, color: 'rgba(0, 82, 217, 0.00)' },
          ]),
        },
      },
    ],
  });
};

watch(activeRange, () => {
  updateChart();
});

const handleResize = () => {
  chartInst?.resize();
};

onMounted(() => {
  window.addEventListener('resize', handleResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
  chartInst?.dispose();
  chartInst = null;
});

// 监听运行状态，动态切换默认模式
watch(
  () => props.isRunning,
  (running) => {
    if (!running) {
      opMode.value = 'api'; // 没开服只能用API改文件
    } else {
      opMode.value = 'command'; // 开服了默认切回指令
    }
  },
  { immediate: true },
);

// 监听弹窗或Tab变化
watch([() => props.visible, activeTab, banType], async ([visible]) => {
  if (route.name !== 'InstanceConsole' || !visible) return;
  fetchCurrentTabData();
});

const fetchCurrentTabData = async () => {
  loading.value = true;
  try {
    if (activeTab.value === 'online') {
      if (props.isRunning) {
        onlinePlayers.value = await getOnlinePlayers(props.serverId);
      } else {
        onlinePlayers.value = [];
      }
    } else if (activeTab.value === 'history') {
      const res = await getHistoryPlayers(props.serverId);
      historyPlayers.value = res.players || [];
      activityChartData.value = res.chartData || [];
      rangeStats.value = res.rangeStats || {};
      nextTick(() => {
        updateChart();
        chartInst?.resize();
      });
    } else if (activeTab.value === 'whitelist') {
      whitelist.value = await getWhitelist(props.serverId);
    } else if (activeTab.value === 'ops') {
      ops.value = await getOps(props.serverId);
    } else if (activeTab.value === 'banned') {
      if (banType.value === 'player') bannedPlayers.value = await getBannedPlayers(props.serverId);
      else bannedIps.value = await getBannedIps(props.serverId);
    }
  } catch (error: any) {
    MessagePlugin.error(`获取数据失败: ${error.message}`);
  } finally {
    loading.value = false;
  }
};

// 命令处理
const handleAction = async (apiCall: () => Promise<any>, cmdString: string, successMsg: string) => {
  try {
    if (opMode.value === 'command') {
      if (!props.isRunning) {
        MessagePlugin.warning('实例未运行，已自动使用 API 模式修改配置');
        opMode.value = 'api';
        await apiCall();
        MessagePlugin.success(successMsg);
        fetchCurrentTabData();
        return;
      }
      await hubStore.sendCommand(cmdString);
      MessagePlugin.success(`已发送指令`);
      setTimeout(() => fetchCurrentTabData(), 1000);
    } else {
      await apiCall();
      MessagePlugin.success(successMsg);
      fetchCurrentTabData();
    }
  } catch (error: any) {
    MessagePlugin.error(`操作失败: ${error.message}`);
  }
};

// 通用纯指令
const sendCmdOnly = async (cmd: string, successMsg: string) => {
  if (!props.isRunning) return MessagePlugin.warning('实例未运行，无法执行控制台指令');
  try {
    await hubStore.sendCommand(cmd);
    MessagePlugin.success(successMsg);
    setTimeout(() => fetchCurrentTabData(), 1500);
  } catch (error: any) {
    MessagePlugin.error(`执行失败: ${error.message}`);
  }
};

// ================= 管理员 (OP) =================
const handleAddOp = async (name: string = inputNewOp.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  await handleAction(() => addOp(props.serverId, name), `op ${name}`, '添加管理员成功');
  if (name === inputNewOp.value) inputNewOp.value = '';
};
const handleRemoveOp = async (name: string) => {
  await handleAction(() => removeOp(props.serverId, name), `deop ${name}`, '移除管理员成功');
};

// ================= 白名单 =================
const handleAddWhitelist = async (name: string = inputNewWhitelist.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  await handleAction(() => addWhitelist(props.serverId, name), `whitelist add ${name}`, '添加白名单成功');
  if (name === inputNewWhitelist.value) inputNewWhitelist.value = '';
};
const handleRemoveWhitelist = async (name: string) => {
  await handleAction(() => removeWhitelist(props.serverId, name), `whitelist remove ${name}`, '移除白名单成功');
};

// ================= 玩家封禁 =================
const handleAddBanPlayer = async (name: string = inputNewBanPlayer.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  const reason = inputNewBanReason.value ? ` ${inputNewBanReason.value}` : '';
  await handleAction(
    () => addBannedPlayer(props.serverId, name, inputNewBanReason.value),
    `ban ${name}${reason}`,
    '封禁玩家成功',
  );
  if (name === inputNewBanPlayer.value) {
    inputNewBanPlayer.value = '';
    inputNewBanReason.value = '';
  }
};
const handleRemoveBanPlayer = async (name: string) => {
  await handleAction(() => removeBannedPlayer(props.serverId, name), `pardon ${name}`, '解封玩家成功');
};

// ================= IP 封禁 =================
const handleAddBanIp = async () => {
  if (!inputNewBanIp.value) return MessagePlugin.warning('请输入IP地址');
  const reason = inputNewBanReason.value ? ` ${inputNewBanReason.value}` : '';
  await handleAction(
    () => addBannedIp(props.serverId, inputNewBanIp.value, inputNewBanReason.value),
    `ban-ip ${inputNewBanIp.value}${reason}`,
    '封禁IP成功',
  );
  inputNewBanIp.value = '';
  inputNewBanReason.value = '';
};
const handleRemoveBanIp = async (ip: string) => {
  await handleAction(() => removeBannedIp(props.serverId, ip), `pardon-ip ${ip}`, '解封IP成功');
};

const handleClose = () => emits('update:visible', false);
</script>

<template>
  <t-dialog
    attach="body"
    :visible="visible"
    header="玩家管理"
    width="min(800px, 95vw)"
    placement="center"
    :footer="false"
    class="player-manager-dialog"
    @close="handleClose"
  >
    <div class="flex flex-col h-[65vh] min-h-[500px]">

      <div class="flex flex-col gap-4 mb-6 shrink-0">

        <div class="flex justify-between items-center">
          <t-tooltip :content="!isRunning ? '未开服时已禁用指令模式，仅支持 API 模式（修改配置文件）' : '指令模式直接与服务端交互，API模式直接修改配置文件'" placement="bottom">
            <t-radio-group v-model="opMode" variant="default-filled" size="small" :disabled="!isRunning" class="!bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-lg p-0.5">
              <t-radio-button value="api">API 模式</t-radio-button>
              <t-radio-button value="command" :disabled="!isRunning">指令优先</t-radio-button>
            </t-radio-group>
          </t-tooltip>

          <t-button variant="text" theme="primary" size="small" :loading="loading" class="!rounded-md hover:!bg-[var(--color-primary)]/10" @click="fetchCurrentTabData">
            <template #icon><refresh-icon /></template> 刷新数据
          </t-button>
        </div>

        <div class="w-full overflow-x-auto hide-scrollbar pb-1">
          <t-radio-group v-model="activeTab" variant="default-filled" class="flex w-max min-w-full !bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-xl p-1">
            <t-radio-button value="history" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><chart-line-data-icon size="14px"/> 玩家数据</div></t-radio-button>
            <t-radio-button value="online" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><user-icon size="14px"/> 在线</div></t-radio-button>
            <t-radio-button value="ops" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><secured-icon size="14px"/> 管理员</div></t-radio-button>
            <t-radio-button value="banned" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><close-circle-icon size="14px"/> 黑名单</div></t-radio-button>
            <t-radio-button value="whitelist" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><usergroup-icon size="14px"/> 白名单</div></t-radio-button>
          </t-radio-group>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto custom-scrollbar pr-2 pb-2">

        <div v-if="activeTab === 'online'" class="flex flex-col gap-3">
          <template v-if="onlinePlayers.length > 0">
            <div v-for="player in onlinePlayers" :key="player" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 hover:border-[var(--color-primary)]/30 transition-colors shadow-sm">
              <div class="flex items-center gap-3">
                <img :src="`https://minotar.net/helm/${player}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated]" />
                <span class="font-bold text-sm text-[var(--td-text-color-primary)]">{{ player }}</span>
              </div>
              <div class="flex flex-wrap items-center gap-1.5">
                <t-button size="small" variant="outline" theme="default" class="!rounded-lg !border-zinc-200 dark:!border-zinc-700 !text-zinc-600 dark:!text-zinc-300 hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)]/50" @click="handleAddOp(player)">设为 OP</t-button>
                <t-button size="small" variant="text" theme="warning" class="!rounded-lg hover:!bg-amber-500/10" @click="handleRemoveOp(player)">撤销 OP</t-button>
                <t-button size="small" variant="text" theme="success" class="!rounded-lg hover:!bg-emerald-500/10" @click="handleAddWhitelist(player)">加白</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="sendCmdOnly(`kick ${player} 被管理员踢出`, `已踢出 ${player}`)">踢出</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="handleAddBanPlayer(player)">封禁</t-button>
              </div>
            </div>
          </template>
          <div v-else class="py-16 flex flex-col items-center justify-center text-[var(--td-text-color-secondary)]">
            <user-clear-icon size="40px" class="mb-3 opacity-60" />
            <span class="text-sm font-medium">{{ isRunning ? '当前没有玩家在线' : '服务器未运行' }}</span>
          </div>
        </div>

        <div v-if="activeTab === 'history'" class="flex flex-col gap-3">
          <!-- 活跃统计卡片与近14天DAU趋势图 -->
          <div class="p-3 bg-zinc-50 dark:bg-zinc-800/50 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-xs flex flex-col md:flex-row items-stretch md:items-center gap-3">
            <div class="flex md:flex-col justify-around gap-2 md:w-32 shrink-0 border-b md:border-b-0 md:border-r border-zinc-200/60 dark:border-zinc-700/60 pb-2 md:pb-0 md:pr-3">
              <div>
                <div class="text-[11px] text-[var(--td-text-color-secondary)]">今日活跃</div>
                <div class="text-base font-bold text-[var(--td-brand-color)]">
                  {{ todayActiveCount }} <span class="text-xs font-normal text-zinc-400">人</span>
                </div>
              </div>
              <div>
                <div class="text-[11px] text-[var(--td-text-color-secondary)]">玩家总数</div>
                <div class="text-base font-bold text-[var(--td-text-color-primary)]">
                  {{ historyPlayers.length }} <span class="text-xs font-normal text-zinc-400">人</span>
                </div>
              </div>
            </div>

            <div class="flex-1 flex flex-col min-w-0">
              <div class="flex justify-between items-center mb-1 flex-wrap gap-1.5">
                <span class="text-xs font-bold text-[var(--td-text-color-primary)] flex items-center gap-1 shrink-0">
                  <chart-line-data-icon size="13px" class="text-[var(--td-brand-color)]" />
                  {{ rangeTitle }}
                </span>
                <t-radio-group
                  v-model="activeRange"
                  variant="default-filled"
                  size="small"
                  class="!bg-zinc-200/50 dark:!bg-zinc-900/60 border border-[var(--td-component-border)] !rounded-lg !p-0.5"
                >
                  <t-radio-button
                    v-for="opt in rangeOptions"
                    :key="opt.value"
                    :value="opt.value"
                    class="!text-[11px] !px-2 !py-0.5"
                  >
                    {{ opt.label }}
                  </t-radio-button>
                </t-radio-group>
              </div>
              <div ref="chartRef" class="w-full h-20"></div>
            </div>
          </div>

          <!-- 搜索框 -->
          <div class="flex items-center gap-2">
            <t-input
              v-model="searchHistory"
              placeholder="搜索玩家名或 UUID..."
              clearable
              size="small"
              class="!rounded-lg !w-full"
            >
              <template #prefix-icon>
                <search-icon />
              </template>
            </t-input>
          </div>

          <!-- 玩家列表 -->
          <template v-if="filteredHistoryPlayers.length > 0">
            <div
              v-for="user in filteredHistoryPlayers"
              :key="user.name"
              class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 hover:border-[var(--color-primary)]/30 transition-colors shadow-xs"
            >
              <div class="flex items-center gap-3 min-w-0">
                <img
                  :src="`https://minotar.net/helm/${user.name}/32.png`"
                  class="w-9 h-9 rounded shadow-xs [image-rendering:pixelated] shrink-0"
                />
                <div class="flex flex-col min-w-0 gap-1">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">{{ user.name }}</span>
                    <span
                      v-if="user.lastIp"
                      class="text-[10px] font-mono px-1.5 py-0.5 rounded bg-zinc-200/60 dark:bg-zinc-700/60 text-zinc-600 dark:text-zinc-300"
                    >
                      {{ user.lastIp }}
                    </span>
                    <span
                      v-if="user.loginCount"
                      class="text-[10px] font-medium px-1.5 py-0.5 rounded bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-300"
                    >
                      登录 {{ user.loginCount }} 次
                    </span>
                  </div>
                  <div class="flex items-center gap-3 text-[11px] text-zinc-500 font-mono flex-wrap">
                    <span v-if="user.lastLoginTime" class="flex items-center gap-1 text-[var(--td-text-color-secondary)]">
                      <time-icon size="12px" /> 上次登录: {{ user.lastLoginTime }}
                    </span>
                    <span v-else class="text-[var(--td-text-color-placeholder)]">暂无登录时间记录</span>
                    <span v-if="user.uuid" class="text-zinc-400 truncate">UUID: {{ user.uuid.split('-')[0] }}...</span>
                  </div>
                </div>
              </div>
              <div class="flex flex-wrap items-center gap-1.5 shrink-0 self-end sm:self-auto">
                <t-button size="small" variant="outline" theme="default" class="!rounded-lg !border-zinc-200 dark:!border-zinc-700 !text-zinc-600 dark:!text-zinc-300 hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)]/50" @click="handleAddOp(user.name)">设为 OP</t-button>
                <t-button size="small" variant="text" theme="success" class="!rounded-lg hover:!bg-emerald-500/10" @click="handleAddWhitelist(user.name)">加白名单</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="handleAddBanPlayer(user.name)">封禁</t-button>
              </div>
            </div>
          </template>
          <div v-else class="py-12 flex flex-col items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">
            <user-clear-icon size="36px" class="mb-2 opacity-50" />
            <span>{{ searchHistory ? '未找到匹配的玩家' : '暂无玩家数据记录' }}</span>
          </div>
        </div>

        <div v-if="activeTab === 'ops'" class="flex flex-col gap-3">
          <div class="flex flex-col sm:flex-row gap-2 mb-2">
            <t-input v-model="inputNewOp" placeholder="输入玩家游戏ID" @enter="handleAddOp()" clearable class="!flex-1" />
            <t-button theme="primary" @click="handleAddOp()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 添加管理员</t-button>
          </div>

          <template v-if="ops.length > 0">
            <div v-for="op in ops" :key="op.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm">
              <div class="flex items-center gap-3">
                <img :src="`https://minotar.net/helm/${op.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated]" />
                <div class="flex flex-col gap-1">
                  <span class="font-bold text-sm text-[var(--td-text-color-primary)]">{{ op.name }}</span>
                  <span class="text-[10px] font-extrabold bg-blue-50 text-blue-600 ring-1 ring-inset ring-blue-500/20 dark:bg-blue-500/10 dark:text-blue-400 dark:ring-blue-500/30 px-1.5 py-0.5 rounded w-max">LV.{{ op.level }}</span>
                </div>
              </div>
              <t-popconfirm content="确定要撤销该管理员吗？" theme="danger" @confirm="handleRemoveOp(op.name)">
                <t-button size="small" variant="outline" theme="danger" class="!rounded-lg !border-red-500/30 hover:!bg-red-500/10 self-start sm:self-auto"><template #icon><delete-icon /></template> 移除</t-button>
              </t-popconfirm>
            </div>
          </template>
          <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无管理员记录</div>
        </div>

        <div v-if="activeTab === 'banned'" class="flex flex-col gap-3">
          <div class="mb-2">
            <t-radio-group v-model="banType" variant="default-filled" size="small" class="!bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-lg p-0.5">
              <t-radio-button value="player">玩家封禁</t-radio-button>
              <t-radio-button value="ip">IP 封禁</t-radio-button>
            </t-radio-group>
          </div>

          <div v-if="banType === 'player'" class="flex flex-col gap-3">
            <div class="flex flex-col sm:flex-row gap-2">
              <t-input v-model="inputNewBanPlayer" placeholder="输入玩家ID" clearable class="!flex-1" />
              <t-input v-model="inputNewBanReason" placeholder="封禁理由(可选)" clearable class="!flex-[1.5]" />
              <t-button theme="danger" @click="handleAddBanPlayer()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 封禁</t-button>
            </div>

            <template v-if="bannedPlayers.length > 0">
              <div v-for="player in bannedPlayers" :key="player.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-red-50/50 dark:bg-red-950/20 rounded-xl border border-red-200/60 dark:border-red-900/40 shadow-sm">
                <div class="flex items-start sm:items-center gap-3 min-w-0">
                  <img :src="`https://minotar.net/helm/${player.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated] shrink-0" />
                  <div class="flex flex-col min-w-0 gap-0.5">
                    <span class="font-bold text-sm text-red-600 dark:text-red-400 truncate">{{ player.name }}</span>
                    <span class="text-[11px] text-[var(--td-text-color-secondary)] mt-0.5 break-all line-clamp-2">理由: {{ player.reason }}</span>
                  </div>
                </div>
                <t-popconfirm content="确定要解封吗？" theme="warning" @confirm="handleRemoveBanPlayer(player.name)">
                  <t-button size="small" variant="outline" theme="primary" class="!rounded-lg !border-[var(--color-primary)]/30 hover:!bg-[var(--color-primary)]/10 shrink-0 self-end sm:self-auto">解封</t-button>
                </t-popconfirm>
              </div>
            </template>
            <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无被封禁的玩家</div>
          </div>

          <div v-else class="flex flex-col gap-3">
            <div class="flex flex-col sm:flex-row gap-2">
              <t-input v-model="inputNewBanIp" placeholder="输入IP地址" clearable class="!flex-1" />
              <t-input v-model="inputNewBanReason" placeholder="封禁理由(可选)" clearable class="!flex-[1.5]" />
              <t-button theme="danger" @click="handleAddBanIp()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 封禁IP</t-button>
            </div>

            <template v-if="bannedIps.length > 0">
              <div v-for="ban in bannedIps" :key="ban.ip" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-red-50/50 dark:bg-red-950/20 rounded-xl border border-red-200/60 dark:border-red-900/40 shadow-sm">
                <div class="flex flex-col min-w-0 gap-0.5">
                  <span class="font-mono font-bold text-sm text-red-600 dark:text-red-400 truncate">{{ ban.ip }}</span>
                  <span class="text-[11px] text-[var(--td-text-color-secondary)] break-all line-clamp-2">理由: {{ ban.reason }}</span>
                </div>
                <t-popconfirm content="确定要解封该IP吗？" theme="warning" @confirm="handleRemoveBanIp(ban.ip)">
                  <t-button size="small" variant="outline" theme="primary" class="!rounded-lg !border-[var(--color-primary)]/30 hover:!bg-[var(--color-primary)]/10 shrink-0 self-end sm:self-auto">解封</t-button>
                </t-popconfirm>
              </div>
            </template>
            <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无被封禁的IP</div>
          </div>
        </div>

        <div v-if="activeTab === 'whitelist'" class="flex flex-col gap-3">
          <div class="flex flex-col sm:flex-row gap-2 mb-2">
            <t-input v-model="inputNewWhitelist" placeholder="输入玩家ID" @enter="handleAddWhitelist()" clearable class="!flex-1" />
            <t-button theme="primary" @click="handleAddWhitelist()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 添加白名单</t-button>
          </div>

          <template v-if="whitelist.length > 0">
            <div v-for="user in whitelist" :key="user.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm">
              <div class="flex items-center gap-3 min-w-0">
                <img :src="`https://minotar.net/helm/${user.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated] shrink-0" />
                <span class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">{{ user.name }}</span>
              </div>
              <t-popconfirm content="移出白名单？" theme="danger" @confirm="handleRemoveWhitelist(user.name)">
                <t-button size="small" variant="outline" theme="danger" class="!rounded-lg !border-red-500/30 hover:!bg-red-500/10 self-start sm:self-auto"><template #icon><delete-icon /></template> 移除</t-button>
              </t-popconfirm>
            </div>
          </template>
          <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">白名单为空</div>
        </div>

      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

.hide-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
  &::-webkit-scrollbar {
    display: none;
  }
}

.custom-scrollbar {
  .scrollbar-mixin();
}
</style>
