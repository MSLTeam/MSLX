<script setup lang="ts">
import {
  CloudIcon, CodeIcon, InternetIcon, LinkIcon,
  PlayCircleIcon, RefreshIcon, ServerIcon, StopCircleIcon,
} from 'tdesign-icons-vue-next';
import { TunnelInfoModel } from '@/api/model/frp';

// 定义 Props
defineProps<{
  frpId: number;
  isRunning: boolean;
  loading: boolean;
  tunnelInfo: TunnelInfoModel | null;
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
          {{ isRunning ? '正常运行' : '未运行' }}
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
          <template #icon><play-circle-icon /></template>启动服务
        </t-button>
        <t-button
          v-else
          theme="danger"
          size="large"
          block
          :loading="loading"
          @click="$emit('stop')"
        >
          <template #icon><stop-circle-icon /></template>停止服务
        </t-button>
        <t-button
          style="margin: 0"
          variant="dashed"
          block
          @click="$emit('clear-log')"
        >
          <template #icon><refresh-icon /></template>清空日志
        </t-button>
      </div>
    </t-card>

    <t-card title="隧道概览" class="info-card" :bordered="false">
      <template #actions>
        <t-tag v-if="tunnelInfo && tunnelInfo.proxies && tunnelInfo.proxies.length > 0 && tunnelInfo.proxies.some(proxy => proxy.type === 'xtcp')" variant="light-outline" theme="primary">联机房间 - 房主</t-tag>
        <t-tag v-else-if="tunnelInfo && tunnelInfo.proxies && tunnelInfo.proxies.length > 0 && tunnelInfo.proxies.some(proxy => proxy.type === 'xtcp - Visitors')" variant="light-outline" theme="primary">联机房间 - 访客</t-tag>
        <t-button v-else shape="circle" variant="text"><code-icon /></t-button>
      </template>

      <div class="info-list">
        <div class="info-item">
          <div class="label"><server-icon /> 隧道实例 ID</div>
          <div class="value">#{{ frpId }}</div>
        </div>

        <template v-if="tunnelInfo && tunnelInfo.proxies && tunnelInfo.proxies.length > 0">
          <div v-for="(proxy, index) in tunnelInfo.proxies" :key="index" class="proxy-group">
            <div v-if="tunnelInfo.proxies.length > 1" class="proxy-header">配置 #{{ index + 1 }}</div>

            <div class="info-item">
              <div class="label"><cloud-icon /> {{ proxy.type.includes('xtcp') ? '房间号':'隧道名称'}}</div>
              <t-tooltip :content="proxy.proxyName" placement="top" show-arrow destroy-on-close>
                <div class="value remote-addr pointer">{{ proxy.proxyName }}</div>
              </t-tooltip>
            </div>

            <div class="info-item">
              <div class="label"><internet-icon /> 协议类型</div>
              <div class="value highlight">{{ proxy.type.toUpperCase() }}</div>
            </div>

            <div class="info-item">
              <div class="label"><link-icon /> {{ proxy.type.includes('xtcp') ? '房间密钥':'连接地址'}}</div>
              <t-tooltip :content="proxy.remoteAddressMain" placement="top" show-arrow destroy-on-close>
                <div class="value remote-addr pointer">{{ proxy.remoteAddressMain || '获取中...' }}</div>
              </t-tooltip>
            </div>

            <div v-if="proxy.remoteAddressBackup && proxy.remoteAddressBackup !== proxy.remoteAddressMain" class="info-item">
              <div class="label"><link-icon /> 备用地址</div>
              <t-tooltip :content="proxy.remoteAddressBackup" placement="top" show-arrow destroy-on-close>
                <div class="value remote-addr pointer">{{ proxy.remoteAddressBackup }}</div>
              </t-tooltip>
            </div>

            <div class="info-item">
              <div class="label"><code-icon /> 本地地址</div>
              <div class="value">{{ proxy.localAddress }}</div>
            </div>
          </div>
        </template>

        <div v-else class="info-item empty-state">
          <div class="label">状态</div>
          <div class="value">{{ loading ? '正在加载配置...' : '暂无详细信息' }}</div>
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
      &:first-child { border-top: none; padding-top: 0; }
    }
    .proxy-header { font-size: 12px; font-weight: bold; color: var(--td-text-color-secondary); }

    .info-item {
      display: flex; justify-content: space-between; align-items: center;
      .label { display: flex; align-items: center; gap: 8px; color: var(--td-text-color-placeholder); font-size: 13px; }
      .value {
        font-family: var(--td-font-family-number); font-weight: 500;
        color: var(--td-text-color-primary); font-size: 13px;
        &.highlight { color: var(--td-brand-color); background: var(--td-brand-color-light); padding: 2px 8px; border-radius: 4px; font-size: 12px; }
        &.remote-addr {
          max-width: 140px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer;
          &:hover { color: var(--td-brand-color); }
        }
        &.pointer { cursor: pointer; }
      }
    }
    .empty-state { padding: 12px 0; color: var(--td-text-color-placeholder); }
  }
}
</style>
