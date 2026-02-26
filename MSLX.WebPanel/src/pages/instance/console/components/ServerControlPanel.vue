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
  <div class="sidebar-content">
    <t-card :bordered="false" class="control-card">
      <div class="control-header">
        <div class="status-indicator" :class="statusConfig.theme">
          <span class="pulse"></span>{{ statusConfig.text }}
        </div>
        <t-tag :theme="statusConfig.theme as any" variant="light" shape="round">
          {{ statusConfig.label }}
        </t-tag>
      </div>

      <div class="control-actions">
        <t-button
          v-if="status === 0"
          theme="primary"
          size="large"
          block
          :loading="loading"
          @click="
            $emit('clear-log');
            $emit('start');
          "
        >
          <template #icon><play-circle-icon /></template>启动实例
        </t-button>

        <template v-else>
          <div v-if="status === 2 && !loading" class="running-action-group">
            <t-popconfirm content="确定要停止该实例吗？" @confirm="$emit('stop')">
              <t-button class="stop-btn" theme="danger" size="large">
                <template #icon><stop-circle-icon /></template>
                停止
              </t-button>
            </t-popconfirm>

            <t-popconfirm content="确定要重启该实例吗？" @confirm="$emit('restart')">
              <t-button theme="warning" size="large" shape="square">
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
              class="force-kill-btn glass-btn"
              :theme="loading ? 'primary' : 'danger'"
              variant="outline"
              block
              :loading="loading"
            >
              <template #icon><close-circle-icon v-if="!loading" /></template>
              {{ loading ? '正在处理...' : '强制结束' }}
            </t-button>
          </t-popconfirm>
        </template>

        <div class="action-row">
          <t-button class="glass-btn" variant="outline" block @click="changeUrl(`/instance/files/${serverId}`)">
            <template #icon><folder-icon /></template>文件管理
          </t-button>

          <t-button class="glass-btn" variant="outline" block @click="handleOpenSettings">
            <template #icon><setting-icon /></template>实例设置
          </t-button>
        </div>

        <t-dropdown trigger="click" :min-column-width="120" placement="bottom">
          <t-button theme="primary" class="glass-btn" variant="outline" block>
            <template #icon><more-icon /></template>更多功能
          </t-button>

          <t-dropdown-menu>
            <t-dropdown-item @click="$emit('clear-log')">
              <template #prefix-icon><refresh-icon /></template>
              清空日志
            </t-dropdown-item>

            <t-dropdown-item :disabled="status !== 2 || loading" @click="$emit('backup')">
              <template #prefix-icon><cloud-icon /></template>
              立即备份
            </t-dropdown-item>
            <t-dropdown-item @click="showMapRenderDialog = true">
              <template #prefix-icon><map-icon /></template>
              世界渲染图
            </t-dropdown-item>
            <t-dropdown-item @click="showLogAnalysisDialog = true">
              <template #prefix-icon><analytics-icon /></template>
              日志分析
            </t-dropdown-item>
          </t-dropdown-menu>
        </t-dropdown>
      </div>
    </t-card>

    <t-card :bordered="false" title="实例概览" class="info-card">
      <template #actions>
        <t-radio-group v-model="activeTab" variant="default-filled" size="small">
          <t-radio-button value="info"><info-circle-icon /> 详情</t-radio-button>
          <t-radio-button value="monitor"><chart-bar-icon /> 监控</t-radio-button>
        </t-radio-group>
      </template>

      <div class="card-content-area">
        <div v-if="activeTab === 'info'" class="info-list">
          <div class="info-item">
            <div class="label"><desktop-icon /> 实例名称</div>
            <div class="value">{{ serverInfo?.name }}</div>
          </div>
          <template v-if="serverInfo?.java !== 'none'">
            <div class="info-item">
              <div class="label"><dashboard-icon /> 内存限制</div>
              <div class="value">{{ serverInfo?.maxM }} MB</div>
            </div>
            <div class="proxy-group"></div>
            <div class="info-item">
              <div class="label"><enter-icon /> 运行端口</div>
              <div class="value">{{ serverInfo?.mcConfig?.serverPort }}</div>
            </div>
            <div class="info-item">
              <div class="label"><arrow-left-right-1-icon /> 游戏难度</div>
              <div class="value">
                <t-tag theme="primary">{{ serverInfo?.mcConfig?.difficulty }}</t-tag>
              </div>
            </div>
            <div class="info-item">
              <div class="label"><wink-icon /> 游戏模式</div>
              <div class="value">
                <t-tag>{{ serverInfo?.mcConfig?.gamemode }}</t-tag>
              </div>
            </div>
            <div class="info-item">
              <div class="label"><folder-icon /> 游戏地图</div>
              <div class="value">{{ serverInfo?.mcConfig?.levelName }}</div>
            </div>
            <div class="info-item">
              <div class="label"><user-unlocked-icon /> 正版验证</div>
              <div class="value">
                <t-tag :theme="serverInfo?.mcConfig?.onlineMode === 'true' ? 'success' : 'warning'">{{
                  serverInfo?.mcConfig?.onlineMode === 'true' ? '开启' : '关闭'
                }}</t-tag>
              </div>
            </div>
          </template>
          <template v-else
            ><div class="info-item">
              <div class="label"><dashboard-icon /> 模式</div>
              <div class="value">自定义模式</div>
            </div></template
          >
          <div class="proxy-group"></div>
          <div class="info-item">
            <div class="label"><time-icon /> 运行时长</div>
            <div class="value">{{ status === 2 ? formattedUptime : '--:--:--' }}</div>
          </div>
        </div>

        <div v-else-if="activeTab === 'monitor'" class="monitor-view">
          <instance-monitor
            v-if="serverInfo && status !== 0"
            :server-id="serverId"
            :is-running="status === 2"
            :max-memory="serverInfo.java === 'none' ? 0 : serverInfo.maxM || 4096"
          />
          <div v-else class="monitor-placeholder">实例未运行</div>
        </div>
      </div>
    </t-card>

    <player-list-card v-if="serverInfo?.monitorPlayers" :server-id="serverId" :status="status" />

    <instance-settings ref="settingsRef" :server-id="serverId" @success="handleSettingsSaved" />
    <log-analysis-dialog v-model:visible="showLogAnalysisDialog" :server-id="serverId" />
    <map-render v-model:visible="showMapRenderDialog" :server-id="serverId" />
  </div>
</template>

<style scoped lang="less">
.sidebar-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  height: 100%;
}

.control-card,
.info-card {
  :deep(.t-card__body) {
    padding-top: 10px;
  }
}

/* === 卡片 1: Radio 按钮透明化 === */
.info-card {
  :deep(.t-radio-group) {
    background-color: transparent !important; /* 去掉整个条的背景 */
  }
  :deep(.t-radio-button) {
    background-color: transparent !important; /* 去掉按钮背景 */
    border: 1px solid transparent; /* 默认无边框 */
    color: var(--td-text-color-secondary);
    transition: all 0.2s;

    /* 选中状态 */
    &.t-is-checked {
      background-color: color-mix(in srgb, var(--td-brand-color), transparent 85%) !important;
      border-color: var(--td-brand-color) !important;
      color: var(--td-brand-color) !important;
      box-shadow: none !important; /* 去掉默认阴影 */
    }

    /* 悬浮状态 */
    &:hover:not(.t-is-checked) {
      color: var(--td-text-color-primary);
      background-color: color-mix(in srgb, var(--td-text-color-primary), transparent 95%) !important;
    }
  }
}

/* === 卡片 2: 控制区域 === */
.control-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
  .status-indicator {
    display: flex;
    align-items: center;
    gap: 10px;
    font-weight: 600;
    color: var(--td-text-color-secondary);
    .pulse {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--td-bg-color-component-disabled);
      transition: all 0.3s;
    }

    /* 状态 2: 运行中 (绿色) */
    &.success {
      color: var(--td-success-color);
      .pulse {
        background: var(--td-success-color);
        box-shadow: 0 0 8px var(--td-success-color);
      }
    }

    /* 状态 3: 停止中  */
    &.warning {
      color: var(--td-warning-color);
      .pulse {
        background: var(--td-warning-color);
        box-shadow: 0 0 8px var(--td-warning-color);
      }
    }

    /* 状态 1 & 4: 启动/重启中 */
    &.primary {
      color: var(--td-brand-color);
      .pulse {
        background: var(--td-brand-color);
        box-shadow: 0 0 8px var(--td-brand-color);
      }
    }
  }
}

.control-actions {
  display: flex;
  flex-direction: column;
  gap: 16px;
  .action-row {
    display: flex;
    gap: 12px;
  }

  :deep(.glass-btn) {
    margin: 0;
    backdrop-filter: blur(4px);
    transition: all 0.2s;

    &.t-is-disabled {
      background-color: color-mix(in srgb, var(--td-bg-color-component-disabled), transparent 50%) !important;
      border-color: var(--td-component-border) !important;
      color: var(--td-text-color-disabled) !important;
      cursor: not-allowed !important;
      &:hover {
        background-color: color-mix(in srgb, var(--td-bg-color-component-disabled), transparent 50%) !important;
        border-color: var(--td-component-border) !important;
        color: var(--td-text-color-disabled) !important;
      }
    }

    &.t-button--theme-default {
      background-color: color-mix(in srgb, var(--td-text-color-primary), transparent 92%) !important;
      border-color: var(--td-component-border);
      color: var(--td-text-color-primary);

      &:hover {
        background-color: color-mix(in srgb, var(--td-text-color-primary), transparent 85%) !important;
        border-color: var(--td-text-color-primary);
      }
    }

    &.t-button--theme-warning {
      background-color: color-mix(in srgb, var(--td-warning-color), transparent 90%) !important;
      border-color: var(--td-warning-color) !important;
      color: var(--td-warning-color) !important;

      &:hover {
        background-color: color-mix(in srgb, var(--td-warning-color), transparent 80%) !important;
      }
    }

    &.t-button--theme-success {
      background-color: color-mix(in srgb, var(--td-success-color), transparent 90%) !important;
      border-color: var(--td-success-color) !important;
      color: var(--td-success-color) !important;

      &:hover {
        background-color: color-mix(in srgb, var(--td-success-color), transparent 80%) !important;
      }
    }

    &.t-button--theme-danger {
      background-color: color-mix(in srgb, var(--td-error-color), transparent 90%) !important;
      border-color: var(--td-error-color) !important;
      color: var(--td-error-color) !important;

      &:hover {
        background-color: color-mix(in srgb, var(--td-error-color), transparent 80%) !important;
      }
    }

    &.t-button--theme-primary {
      background-color: color-mix(in srgb, var(--td-brand-color), transparent 90%) !important;
      border-color: var(--td-brand-color) !important;
      color: var(--td-brand-color) !important;

      &:hover {
        background-color: color-mix(in srgb, var(--td-brand-color), transparent 80%) !important;
      }
    }
  }
  // 强制退出
  .force-kill-btn {
    margin-top: -8px;
    margin-left: 0;
    animation: fadeIn 0.3s ease-in-out;
  }

  .running-action-group {
    display: flex;

    .stop-btn {
      flex: 1;
    }
  }

  @keyframes fadeIn {
    from {
      opacity: 0;
      transform: translateY(-5px);
    }
    to {
      opacity: 1;
      transform: translateY(0);
    }
  }
}

.monitor-placeholder {
  color: var(--td-text-color-placeholder);
  text-align: center;
  padding: 40px 0;
  font-size: 13px;
}

.info-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  .proxy-group {
    border-top: 1px dashed var(--td-component-stroke);
    margin-top: 4px;
  }
  .info-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    .label {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--td-text-color-placeholder);
      font-size: 13px;
    }
    .value {
      font-family: var(--td-font-family-number);
      font-weight: 500;
      font-size: 13px;
    }
  }
}
</style>
