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
  CloudIcon,
} from 'tdesign-icons-vue-next';
import { InstanceInfoModel } from '@/api/model/instance';

import InstanceSettings from './InstanceSettings.vue';
import { changeUrl } from '@/router';

// --- Props & Emits ---
const props = defineProps<{
  serverId: number;
  isRunning: boolean;
  loading: boolean;
  serverInfo: InstanceInfoModel;
}>();

const emits = defineEmits<{
  start: [];
  stop: [];
  'clear-log': [];
  'refresh-info': [];
  backup: [];
}>();

const settingsRef = ref<InstanceType<typeof InstanceSettings> | null>(null);

// 设置按钮
const handleOpenSettings = () => {
  if (settingsRef.value) {
    settingsRef.value.open();
  }
};

// 子组件保存成功后的回调
const handleSettingsSaved = () => {
  emits('refresh-info');
};

const runSeconds = ref(0);
let timer: number | null = null;

const parseTimeSpanToSeconds = (timeStr?: string) => {
  if (!timeStr) return 0;
  const match = timeStr.match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/);
  if (match) {
    const days = parseInt(match[1] || '0', 10);
    return days * 86400 + parseInt(match[2]) * 3600 + parseInt(match[3]) * 60 + parseInt(match[4]);
  }
  return 0;
};

const formattedUptime = computed(() => {
  if (runSeconds.value <= 0) return '00:00:00';
  const days = Math.floor(runSeconds.value / 86400);
  const hours = Math.floor((runSeconds.value % 86400) / 3600);
  const minutes = Math.floor((runSeconds.value % 3600) / 60);
  const seconds = Math.floor(runSeconds.value % 60);
  const pad = (n: number) => n.toString().padStart(2, '0');
  const t = `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
  return days > 0 ? `${days}天 ${t}` : t;
});

const startTimer = () => {
  if (timer) clearInterval(timer);
  timer = window.setInterval(() => runSeconds.value++, 1000);
};
const stopTimer = () => {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
};

watch(
  () => props.serverInfo?.uptime,
  (v) => v && (runSeconds.value = parseTimeSpanToSeconds(v)),
  { immediate: true },
);
watch(
  () => props.isRunning,
  (v) => (v ? !timer && startTimer() : stopTimer()),
  { immediate: true },
);
onUnmounted(() => stopTimer());
</script>

<template>
  <div class="sidebar-content">
    <t-card class="control-card" :bordered="false">
      <div class="control-header">
        <div class="status-indicator" :class="{ running: isRunning }">
          <span class="pulse"></span>{{ isRunning ? 'Running' : 'Stopped' }}
        </div>
        <t-tag :theme="isRunning ? 'success' : 'default'" variant="light" shape="round">
          {{ isRunning ? '运行中' : '已停止' }}
        </t-tag>
      </div>

      <div class="control-actions">
        <t-button
          v-if="!isRunning"
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
        <t-button v-else theme="danger" size="large" block :loading="loading" @click="$emit('stop')">
          <template #icon><stop-circle-icon /></template>停止实例
        </t-button>

        <div class="action-row">
          <t-button variant="outline" block @click="changeUrl(`/instance/files/${serverId}`)">
            <template #icon><folder-icon /></template>文件管理
          </t-button>

          <t-button
            variant="outline"
            block
            :disabled="!isRunning"
            :loading="loading"
            @click="$emit('backup')"
          >
            <template #icon><cloud-icon /></template>备份存档
          </t-button>
        </div>

        <div class="action-row">
          <t-button variant="outline" theme="warning" block @click="$emit('clear-log')">
            <template #icon><refresh-icon /></template>清空
          </t-button>

          <t-button variant="outline" theme="primary" block @click="handleOpenSettings">
            <template #icon><setting-icon /></template>设置
          </t-button>
        </div>
      </div>
    </t-card>

    <t-card v-if="serverInfo?.java !== 'none'" title="实例详情" class="info-card" :bordered="false">
      <div class="info-list">
        <div class="info-item">
          <div class="label"><desktop-icon /> 实例名称</div>
          <div class="value">{{ serverInfo?.name || 'Minecraft Server' }}</div>
        </div>
        <div class="info-item">
          <div class="label"><dashboard-icon /> 内存限制</div>
          <div class="value">{{ serverInfo?.maxM || '?' }} MB</div>
        </div>
        <div class="proxy-group"></div>
        <div class="info-item">
          <div class="label"><enter-icon /> 运行端口</div>
          <div class="value">{{ serverInfo?.mcConfig?.serverPort || '?' }}</div>
        </div>
        <div class="info-item">
          <div class="label"><arrow-left-right-1-icon /> 游戏难度</div>
          <div class="value"><t-tag theme="primary">{{ serverInfo?.mcConfig?.difficulty || '?' }}</t-tag></div>
        </div>
        <div class="info-item">
          <div class="label"><wink-icon /> 游戏模式</div>
          <div class="value"><t-tag>{{ serverInfo?.mcConfig?.gamemode || '?' }}</t-tag></div>
        </div>
        <div class="info-item">
          <div class="label"><folder-icon /> 游戏地图</div>
          <div class="value">{{ serverInfo?.mcConfig?.levelName || '?' }}</div>
        </div>
        <div class="info-item">
          <div class="label"><user-unlocked-icon /> 正版验证</div>
          <div class="value"><t-tag :theme="serverInfo?.mcConfig?.onlineMode === 'true' ? 'success' : 'warning'">{{ serverInfo?.mcConfig?.onlineMode === 'true' ? '已开启' : '已关闭' }}</t-tag></div>
        </div>

        <div class="proxy-group"></div>
        <div class="info-item">
          <div class="label"><time-icon /> 运行时长</div>
          <div class="value">{{ isRunning ? formattedUptime : '--:--:--' }}</div>
        </div>
      </div>
    </t-card>

    <t-card v-else title="实例详情" class="info-card" :bordered="false">
      <div class="info-list">
        <div class="info-item">
          <div class="label"><desktop-icon /> 实例名称</div>
          <div class="value">{{ serverInfo?.name || 'Minecraft Server' }}</div>
        </div>
        <div class="info-item">
          <div class="label"><dashboard-icon /> 模式</div>
          <div class="value">自定义模式</div>
        </div>

        <div class="proxy-group"></div>
        <div class="info-item">
          <div class="label"><time-icon /> 运行时长</div>
          <div class="value">{{ isRunning ? formattedUptime : '--:--:--' }}</div>
        </div>
      </div>
    </t-card>

    <instance-settings ref="settingsRef" :server-id="serverId" @success="handleSettingsSaved" />
  </div>
</template>

<style scoped lang="less">
.sidebar-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.control-card,
.info-card {
  border-radius: 12px;
  box-shadow: var(--td-shadow-1);
  background: var(--td-bg-color-container);
}

.control-card {
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
      &.running {
        color: var(--td-success-color);
        .pulse {
          background: var(--td-success-color);
          box-shadow: 0 0 8px var(--td-success-color);
        }
      }
    }
  }
  .control-actions {
    display: flex;
    flex-direction: column;
    gap: 16px; /* 调整了间距，让按钮排列更舒服 */
    .action-row {
      display: flex;
      gap: 12px;
      .t-button {
        flex: 1;
        margin: 0;
      }
    }
  }
}

.info-card {
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
}
</style>
