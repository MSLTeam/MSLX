<script setup lang="ts">
import {
  CpuIcon, DashboardIcon, DesktopIcon,
  PlayCircleIcon, RefreshIcon, StopCircleIcon,
  TimeIcon
} from 'tdesign-icons-vue-next';
import { copyText } from '@/utils/clipboard';

defineProps<{
  serverId: number;
  isRunning: boolean;
  loading: boolean;
  serverInfo: any | null;
}>();

// 定义 Emits
defineEmits<{
  start: [],
  stop: [],
  'clear-log': []
}>();
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
          @click="$emit('start')"
        >
          <template #icon><play-circle-icon /></template>启动实例
        </t-button>
        <t-button
          v-else
          theme="danger"
          size="large"
          block
          :loading="loading"
          @click="$emit('stop')"
        >
          <template #icon><stop-circle-icon /></template>停止实例
        </t-button>
        <t-button
          style="margin: 0"
          variant="dashed"
          block
          @click="$emit('clear-log')"
        >
          <template #icon><refresh-icon /></template>清空控制台
        </t-button>
      </div>
    </t-card>

    <t-card title="实例详情" class="info-card" :bordered="false">
      <template #actions>
        <t-button shape="circle" variant="text"><dashboard-icon /></t-button>
      </template>

      <div class="info-list">
        <div class="info-item">
          <div class="label"><desktop-icon /> 实例名称</div>
          <div class="value">{{ serverInfo?.name || 'Minecraft Survival' }}</div>
        </div>

        <div class="info-item">
          <div class="label"><cpu-icon /> CPU 核心限制</div>
          <div class="value">{{ serverInfo?.cpuLimit || '2.0' }} Cores</div>
        </div>

        <div class="info-item">
          <div class="label"><dashboard-icon /> 内存分配</div>
          <div class="value">{{ serverInfo?.memoryLimit || '4096' }} MB</div>
        </div>

        <div class="proxy-group">
          <div class="proxy-header">网络信息</div>
          <div class="info-item">
            <div class="label">公网 IP</div>
            <t-tooltip content="点击复制" placement="top">
              <div class="value remote-addr pointer" @click="copyText('123.45.67.89', true)">
                {{ serverInfo?.publicIp || '123.45.67.89' }}
              </div>
            </t-tooltip>
          </div>
          <div class="info-item">
            <div class="label">暴露端口</div>
            <div class="value highlight">{{ serverInfo?.port || '25565' }}</div>
          </div>
        </div>

        <div class="proxy-group">
          <div class="proxy-header">运行状态</div>
          <div class="info-item">
            <div class="label"><time-icon /> 运行时间</div>
            <div class="value">{{ isRunning ? '2h 15m' : '-' }}</div>
          </div>
        </div>

      </div>
    </t-card>
  </div>
</template>

<style scoped lang="less">
.sidebar-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.control-card {
  border-radius: 12px;
  box-shadow: var(--td-shadow-1);
  background: var(--td-bg-color-container);

  .control-header {
    display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px;
    .status-indicator {
      display: flex; align-items: center; gap: 10px; font-weight: 600;
      color: var(--td-text-color-secondary);
      .pulse {
        width: 8px; height: 8px; border-radius: 50%;
        background: var(--td-bg-color-component-disabled); transition: all 0.3s;
      }
      &.running {
        color: var(--td-success-color);
        .pulse { background: var(--td-success-color); box-shadow: 0 0 8px var(--td-success-color); }
      }
    }
  }
  .control-actions {
    display: flex; flex-direction: column; width: 100%; gap: 16px;
  }
}

.info-card {
  border-radius: 12px;
  box-shadow: var(--td-shadow-1);
  background: var(--td-bg-color-container);

  .info-list {
    display: flex; flex-direction: column; gap: 12px;

    .proxy-group {
      display: flex; flex-direction: column; gap: 12px; padding-top: 12px;
      border-top: 1px dashed var(--td-component-stroke);
      margin-top: 4px;
    }
    .proxy-header { font-size: 12px; font-weight: bold; color: var(--td-text-color-secondary); }

    .info-item {
      display: flex; justify-content: space-between; align-items: center;
      .label { display: flex; align-items: center; gap: 8px; color: var(--td-text-color-placeholder); font-size: 13px; }
      .value {
        font-family: var(--td-font-family-number); font-weight: 500;
        color: var(--td-text-color-primary); font-size: 13px;
        &.highlight { color: var(--td-brand-color); background: var(--td-brand-color-light); padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        &.remote-addr { cursor: pointer; &:hover { color: var(--td-brand-color); } }
        &.pointer { cursor: pointer; }
      }
    }
  }
}
</style>
