<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue';
import {
  DashboardIcon,
  DesktopIcon,
  PlayCircleIcon,
  RefreshIcon,
  StopCircleIcon,
  TimeIcon,
  SettingIcon,
  FolderIcon,
  ArrowLeftRight1Icon,
  EnterIcon,
  WinkIcon,
  UserUnlockedIcon,
  ChartBarIcon,
  InfoCircleIcon,
  CloseCircleIcon,
  CloudIcon,
  MoreIcon,
  AnalyticsIcon,
  MapIcon,
} from 'tdesign-icons-vue-next';
import { InstanceInfoModel } from '@/api/model/instance';
import InstanceSettings from './InstanceSettings.vue';
import InstanceMonitor from './InstanceMonitor.vue';
import LogAnalysisDialog from './LogAnalysis.vue';
import PlayerListCard from './PlayerManagerComponents/PlayerListCard.vue';
import { changeUrl } from '@/router';
import MapRender from '@/pages/instance/console/components/MapRender.vue';

// --- Props & Emits ---
const props = defineProps<{
  serverId: number;
  status: number;
  loading: boolean;
  serverInfo?: InstanceInfoModel;
}>();

const emits = defineEmits<{
  start: [];
  stop: [];
  'clear-log': [];
  'refresh-info': [];
  backup: [];
  'force-exit': [];
  restart: [];
}>();

const statusConfig = computed(() => {
  switch (props.status) {
    case 1:
      return { text: 'Starting', label: '启动中', theme: 'primary', pulse: true };
    case 2:
      return { text: 'Running', label: '运行中', theme: 'success', pulse: true };
    case 3:
      return { text: 'Stopping', label: '停止中', theme: 'warning', pulse: true };
    case 4:
      return { text: 'Restarting', label: '重启中', theme: 'primary', pulse: true };
    default:
      return { text: 'Stopped', label: '已停止', theme: 'default', pulse: false };
  }
});

const showLogAnalysisDialog = ref(false);
const showMapRenderDialog = ref(false);

const settingsRef = ref<InstanceType<typeof InstanceSettings> | null>(null);
const activeTab = ref('info');

const handleOpenSettings = () => {
  settingsRef.value?.open();
};
const handleSettingsSaved = () => {
  emits('refresh-info');
};

// --- 计时器逻辑 ---
const runSeconds = ref(0);
let timer: number | null = null;

const parseTimeSpanToSeconds = (timeStr?: string) => {
  if (!timeStr) return 0;
  const match = timeStr.match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/);
  if (match) {
    return (
      parseInt(match[1] || '0', 10) * 86400 + parseInt(match[2]) * 3600 + parseInt(match[3]) * 60 + parseInt(match[4])
    );
  }
  return 0;
};

const formattedUptime = computed(() => {
  if (runSeconds.value <= 0) return '00:00:00';
  const days = Math.floor(runSeconds.value / 86400);
  const t = new Date(runSeconds.value * 1000).toISOString().substr(11, 8);
  return days > 0 ? `${days}天 ${t}` : t;
});

watch(
  () => props.serverInfo?.uptime,
  (v) => v && (runSeconds.value = parseTimeSpanToSeconds(v)),
  { immediate: true },
);

// 几时
watch(
  () => props.status,
  (v) => {
    if (v === 2) {
      if (!timer) timer = window.setInterval(() => runSeconds.value++, 1000);
    } else {
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
    }
  },
  { immediate: true },
);

onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div class="flex flex-col gap-5 h-full">

    <div class="design-card bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-5">

      <div class="flex justify-between items-center mb-5">
        <div class="flex items-center gap-2 font-bold text-sm"
             :class="{
               'text-zinc-500': status === 0,
               'text-[var(--color-primary)]': status === 1 || status === 4,
               'text-[var(--color-success)]': status === 2,
               'text-[var(--color-warning)]': status === 3
             }">
          <span class="relative flex h-2.5 w-2.5">
            <span v-if="status === 1 || status === 2 || status === 4"
                  class="animate-ping absolute inline-flex h-full w-full rounded-full opacity-75"
                  :class="status === 2 ? 'bg-[var(--color-success)]' : 'bg-[var(--color-primary)]'">
            </span>
            <span class="relative inline-flex rounded-full h-2.5 w-2.5"
                  :class="{
                    'bg-zinc-400 dark:bg-zinc-600': status === 0,
                    'bg-[var(--color-primary)]': status === 1 || status === 4,
                    'bg-[var(--color-success)]': status === 2,
                    'bg-[var(--color-warning)]': status === 3
                  }">
            </span>
          </span>
          {{ statusConfig.text }}
        </div>
        <t-tag :theme="statusConfig.theme as any" variant="light" class="!rounded !font-bold">
          {{ statusConfig.label }}
        </t-tag>
      </div>

      <div class="flex flex-col gap-2.5">

        <t-button
          v-if="status === 0"
          theme="primary"
          size="large"
          block
          :loading="loading"
          class="!rounded-lg !h-10 !font-bold shadow-sm"
          @click="$emit('clear-log'); $emit('start');"
        >
          <template #icon><play-circle-icon /></template>启动实例
        </t-button>

        <template v-else>
          <div v-if="status === 2 && !loading" class="flex gap-2 w-full">
            <t-popconfirm content="确定要停止该实例吗？" @confirm="$emit('stop')">
              <t-button theme="danger" class="flex-1 !rounded-lg !h-10 !font-bold shadow-sm">
                <template #icon><stop-circle-icon /></template> 停止
              </t-button>
            </t-popconfirm>

            <t-popconfirm content="确定要重启该实例吗？" @confirm="$emit('restart')">
              <t-button theme="warning" class="!rounded-lg !h-10 !w-10 !p-0 shadow-sm shrink-0">
                <template #icon><refresh-icon /></template>
              </t-button>
            </t-popconfirm>
          </div>

          <t-popconfirm
            v-if="status === 3 || status === 4 || loading"
            content="确定要强制结束吗？可能会导致数据丢失！"
            @confirm="$emit('force-exit')"
          >
            <t-button
              :theme="loading ? 'primary' : 'danger'"
              variant="outline"
              block
              :loading="loading"
              class="!rounded-lg !h-10 !font-bold transition-all duration-300"
              :class="loading ? '!bg-[var(--color-primary)]/10 !border-[var(--color-primary)]/30' : '!bg-red-500/10 !border-red-500/30 !text-red-500 hover:!bg-red-500/20'"
            >
              <template #icon><close-circle-icon v-if="!loading" /></template>
              {{ loading ? '正在处理...' : '强制结束' }}
            </t-button>
          </t-popconfirm>
        </template>

        <div class="flex gap-2 w-full mt-1.5">
          <t-button variant="outline" class="flex-1 !rounded-lg !h-8 !bg-zinc-100 dark:!bg-zinc-800 !border-zinc-200 dark:!border-zinc-700 !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 transition-colors" @click="changeUrl(`/instance/files/${serverId}`)">
            <template #icon><folder-icon /></template>文件管理
          </t-button>

          <t-button variant="outline" class="flex-1 !rounded-lg !h-8 !bg-zinc-100 dark:!bg-zinc-800 !border-zinc-200 dark:!border-zinc-700 !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 transition-colors" @click="handleOpenSettings">
            <template #icon><setting-icon /></template>实例设置
          </t-button>
        </div>

        <t-dropdown trigger="click" :min-column-width="120" placement="bottom">
          <t-button block class="!rounded-lg !h-8 mt-0.5 !bg-[var(--color-primary)]/5 !border-[var(--color-primary)]/20 !text-[var(--color-primary)] hover:!bg-[var(--color-primary)]/10 transition-colors">
            <template #icon><more-icon /></template>更多功能
          </t-button>

          <t-dropdown-menu>
            <t-dropdown-item @click="$emit('clear-log')">
              <template #prefix-icon><refresh-icon /></template>清空日志
            </t-dropdown-item>
            <t-dropdown-item :disabled="status !== 2 || loading" @click="$emit('backup')">
              <template #prefix-icon><cloud-icon /></template>立即备份
            </t-dropdown-item>
            <t-dropdown-item @click="showMapRenderDialog = true">
              <template #prefix-icon><map-icon /></template>世界渲染图
            </t-dropdown-item>
            <t-dropdown-item @click="showLogAnalysisDialog = true">
              <template #prefix-icon><analytics-icon /></template>日志分析
            </t-dropdown-item>
          </t-dropdown-menu>
        </t-dropdown>

      </div>
    </div>

    <div class="design-card flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-5">

      <div class="flex justify-between items-center mb-4 pb-4 border-b border-zinc-200/60 dark:border-zinc-700/60">
        <h3 class="text-sm font-bold text-[var(--td-text-color-primary)] m-0">实例概览</h3>
        <t-radio-group v-model="activeTab" variant="default-filled" size="small">
          <t-radio-button value="info"><info-circle-icon /> 详情</t-radio-button>
          <t-radio-button value="monitor"><chart-bar-icon /> 监控</t-radio-button>
        </t-radio-group>
      </div>

      <div class="flex-1 min-h-0">
        <div v-if="activeTab === 'info'" class="flex flex-col gap-1.5">

          <div class="flex justify-between items-center py-1">
            <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><desktop-icon size="14px" /> 实例名称</div>
            <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate max-w-[150px]">{{ serverInfo?.name }}</div>
          </div>

          <template v-if="serverInfo?.java !== 'none'">
            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><dashboard-icon size="14px" /> 内存限制</div>
              <div class="font-mono text-sm font-bold text-[var(--td-text-color-primary)]">{{ serverInfo?.maxM }} MB</div>
            </div>

            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><enter-icon size="14px" /> 运行端口</div>
              <div class="font-mono text-sm font-bold text-[var(--color-primary)]">{{ serverInfo?.mcConfig?.serverPort }}</div>
            </div>

            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><arrow-left-right-1-icon size="14px" /> 游戏难度</div>
              <t-tag theme="primary" variant="light" size="small" class="!rounded">{{ serverInfo?.mcConfig?.difficulty }}</t-tag>
            </div>

            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><wink-icon size="14px" /> 游戏模式</div>
              <t-tag variant="light" size="small" class="!rounded">{{ serverInfo?.mcConfig?.gamemode }}</t-tag>
            </div>

            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><folder-icon size="14px" /> 游戏地图</div>
              <div class="text-xs font-bold text-[var(--td-text-color-primary)]">{{ serverInfo?.mcConfig?.levelName }}</div>
            </div>

            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><user-unlocked-icon size="14px" /> 正版验证</div>
              <t-tag :theme="serverInfo?.mcConfig?.onlineMode === 'true' ? 'success' : 'warning'" variant="light" size="small" class="!rounded">
                {{ serverInfo?.mcConfig?.onlineMode === 'true' ? '开启' : '关闭' }}
              </t-tag>
            </div>
          </template>

          <template v-else>
            <div class="flex justify-between items-center py-1">
              <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><dashboard-icon size="14px" /> 模式</div>
              <t-tag theme="warning" variant="light" size="small" class="!rounded">自定义模式</t-tag>
            </div>
          </template>

          <div class="flex justify-between items-center py-1 mt-1">
            <div class="flex items-center gap-2 text-xs text-[var(--td-text-color-secondary)]"><time-icon size="14px" /> 运行时长</div>
            <div class="font-mono text-sm font-bold text-[var(--td-text-color-primary)]">{{ status === 2 ? formattedUptime : '--:--:--' }}</div>
          </div>
        </div>

        <div v-else-if="activeTab === 'monitor'" class="flex flex-col">
          <instance-monitor
            v-if="serverInfo && status !== 0"
            :server-id="serverId"
            :is-running="status === 2"
            :max-memory="serverInfo.java === 'none' ? 0 : serverInfo.maxM || 4096"
          />
          <div v-else class="flex-1 flex items-center justify-center text-[var(--td-text-color-secondary)] text-sm font-medium">
            实例未运行
          </div>
        </div>
      </div>
    </div>

    <player-list-card v-if="serverInfo?.monitorPlayers" :server-id="serverId" :status="status" class="design-card" />

    <instance-settings ref="settingsRef" :server-id="serverId" @success="handleSettingsSaved" />
    <log-analysis-dialog v-model:visible="showLogAnalysisDialog" :server-id="serverId" />
    <map-render v-model:visible="showMapRenderDialog" :server-id="serverId" />
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
