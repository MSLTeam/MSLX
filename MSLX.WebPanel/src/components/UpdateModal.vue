<script setup lang="ts">
import { computed } from 'vue';
import { DownloadIcon, BrowseIcon, CloseIcon, CloudDownloadIcon } from 'tdesign-icons-vue-next';

// 定义接口
export interface UpdateInfoModel {
  needUpdate: boolean;
  currentVersion: string;
  latestVersion: string;
  status: string;
  log: string;
}

export interface UpdateDownloadInfoModel {
  web: string;
  file: string;
}

interface Props {
  visible: boolean;
  updateInfo: UpdateInfoModel | null;
  downloadInfo: UpdateDownloadInfoModel | null;
}

const props = defineProps<Props>();
const emit = defineEmits(['close', 'skip']);

// 判断是否为测试版
const isBeta = computed(() => props.updateInfo?.status === 'beta');

// 打开链接
const openLink = (url: string) => {
  if (url) window.open(url, '_blank');
};

// 处理关闭
const handleClose = () => {
  emit('close');
};

// 处理跳过
const handleSkip = () => {
  emit('skip');
};
</script>

<template>
  <t-dialog
    :visible="props.visible"
    :header="false"
    :footer="false"
    :close-on-overlay-click="false"
    :close-btn="false"
    width="500px"
    class="update-modal"
    destroy-on-close
    @close="handleClose"
  >
    <div class="modal-header">
      <div class="header-content">
        <div class="title-row">
          <h3>发现新版本</h3>
          <t-tag v-if="isBeta" theme="warning" variant="light">Beta</t-tag>
          <t-tag v-else theme="success" variant="light">Release</t-tag>
        </div>
        <div class="version-row">
          <t-tag variant="outline" size="small">{{ updateInfo?.currentVersion }}</t-tag>
          <span class="arrow">→</span>
          <t-tag theme="primary" variant="light-outline" size="small">{{ updateInfo?.latestVersion }}</t-tag>
        </div>
      </div>
      <t-button variant="text" shape="circle" @click="handleClose">
        <template #icon><close-icon /></template>
      </t-button>
    </div>

    <div class="modal-body">
      <div class="log-title">更新内容：</div>
      <div class="log-scroll-area">
        <div class="log-text">{{ updateInfo?.log || '暂无详细日志' }}</div>
      </div>
    </div>

    <div class="modal-footer">
      <div class="primary-actions">
        <t-button theme="primary" block :disabled="!downloadInfo?.file" @click="openLink(downloadInfo?.file || '')">
          <template #icon><download-icon /></template>
          下载新版本
        </t-button>

        <div class="secondary-grid">
          <t-button variant="outline" :disabled="!downloadInfo?.web" @click="openLink(downloadInfo?.web || '')">
            <template #icon><browse-icon /></template>
            前往下载页
          </t-button>

          <t-button variant="dashed" disabled title="功能开发中...">
            <template #icon><cloud-download-icon /></template>
            自动更新
          </t-button>
        </div>
      </div>

      <div class="sub-actions">
        <t-link theme="default" hover="color" size="small" @click="handleSkip"> 跳过此版本 </t-link>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.update-modal {
  /* 移动端适配：宽度在手机上自动变宽 */
  @media (max-width: 768px) {
    width: 90% !important;
    max-width: 400px;
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;

  .header-content {
    .title-row {
      display: flex;
      align-items: center;
      gap: 8px;
      h3 {
        margin: 0;
        font-size: 20px;
        font-weight: 600;
        color: var(--td-text-color-primary);
      }
    }
    .version-row {
      margin-top: 8px;
      display: flex;
      align-items: center;
      gap: 8px;
      .arrow {
        color: var(--td-text-color-placeholder);
        font-family: monospace;
      }
    }
  }
}

.modal-body {
  margin-bottom: 24px;

  .log-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--td-text-color-secondary);
    margin-bottom: 8px;
  }

  .log-scroll-area {
    background: var(--td-bg-color-secondarycontainer); /* 适配深色模式 */
    border-radius: var(--td-radius-medium);
    padding: 12px;
    max-height: 200px;
    overflow-y: auto;
    border: 1px solid var(--td-component-border);

    /* 滚动条美化 */
    &::-webkit-scrollbar {
      width: 6px;
    }
    &::-webkit-scrollbar-thumb {
      background: var(--td-scrollbar-color);
      border-radius: 4px;
    }

    .log-text {
      font-family: 'Menlo', 'Monaco', 'Courier New', monospace;
      font-size: 13px;
      line-height: 1.6;
      white-space: pre-wrap;
      color: var(--td-text-color-primary);
    }
  }
}

.modal-footer {
  .primary-actions {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .secondary-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }

  .sub-actions {
    margin-top: 16px;
    display: flex;
    justify-content: center;
  }
}

/* 移动端下按钮变为垂直排列 */
@media (max-width: 480px) {
  .secondary-grid {
    grid-template-columns: 1fr !important;
  }
}
</style>
