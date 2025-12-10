<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { startDecompress, getDeompressStatus } from '@/api/files';

const props = defineProps<{
  visible: boolean;
  instanceId: number;
  currentPath: string;
  fileName: string;
}>();

const emit = defineEmits(['update:visible', 'success']);

const isVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val),
});

const encoding = ref('auto');
const createSubFolder = ref(true);
const isProcessing = ref(false);
const progress = ref(0);
const statusMessage = ref('');
const taskId = ref('');
let pollTimer: number | null = null;

const encodingOptions = [
  { label: '自动检测 (推荐)', value: 'auto' },
  { label: 'UTF-8 (Linux/Mac通用)', value: 'utf-8' },
  { label: 'GBK (Windows传统)', value: 'gbk' },
];

const resetState = () => {
  encoding.value = 'auto';
  createSubFolder.value = true;
  isProcessing.value = false;
  progress.value = 0;
  statusMessage.value = '';
  taskId.value = '';
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
};

watch(() => props.visible, (val) => {
  if (val) resetState();
});

const stopPolling = () => {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
};

const handleStart = async () => {
  if (isProcessing.value) return;

  try {
    isProcessing.value = true;
    statusMessage.value = '正在提交任务...';

    // 拦截器已处理 .data，直接获取响应体
    const res: any = await startDecompress(
      props.instanceId,
      props.fileName,
      props.currentPath,
      encoding.value,
      createSubFolder.value
    );

    // 修正：小写 taskId，直接访问
    if (res && res.taskId) {
      taskId.value = res.taskId;
      pollTimer = window.setInterval(pollStatus, 1000);
    } else {
      throw new Error('未能获取任务ID');
    }
  } catch (error: any) {
    MessagePlugin.error(error.message || '提交失败');
    isProcessing.value = false;
  }
};

const pollStatus = async () => {
  if (!taskId.value) return;
  try {
    // 拦截器已处理，直接获取对象
    const res: any = await getDeompressStatus(taskId.value);

    progress.value = res.progress || 0;
    statusMessage.value = res.message;

    if (res.status === 'success') {
      stopPolling();
      MessagePlugin.success('解压成功');
      progress.value = 100;
      setTimeout(() => {
        isVisible.value = false;
        emit('success');
      }, 800);
    } else if (res.status === 'error') {
      stopPolling();
      isProcessing.value = false;
      MessagePlugin.error(res.message || '解压出错');
    }
  } catch (error) {
    console.error('轮询失败', error);
  }
};

const handleClose = () => {
  if (isProcessing.value && progress.value < 100) {
    MessagePlugin.warning('解压正在后台进行中');
  }
  stopPolling();
  isVisible.value = false;
};
</script>

<template>
  <t-dialog
    v-model:visible="isVisible"
    header="解压文件"
    :footer="false"
    :close-on-overlay-click="!isProcessing"
    :on-close="handleClose"
    width="480px"
  >
    <div class="decompress-container">
      <div class="file-info">
        <span class="label">目标文件：</span>
        <span class="value">{{ fileName }}</span>
      </div>

      <div class="form-item" v-if="!isProcessing">
        <span class="label">文件名编码</span>
        <t-select v-model="encoding" :options="encodingOptions" />
      </div>

      <div class="switch-row" v-if="!isProcessing">
        <div class="switch-info">
          <span class="switch-label">创建同名文件夹</span>
          <span class="switch-tip">推荐开启，防止文件散乱在当前目录</span>
        </div>
        <t-switch v-model="createSubFolder" />
      </div>

      <div class="progress-area" v-if="isProcessing">
        <div class="status-header">
          <span class="status-text">{{ statusMessage }}</span>
          <span class="status-percent">{{ progress }}%</span>
        </div>
        <t-progress
          theme="line"
          :percentage="progress"
          :label="false"
          :status="progress === 100 ? 'success' : 'active'"
        />
      </div>

      <div class="actions" v-if="!isProcessing">
        <t-button variant="outline" @click="handleClose">取消</t-button>
        <t-button theme="primary" @click="handleStart">开始解压</t-button>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.decompress-container {
  padding: 10px 0;
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.file-info {
  background: var(--td-bg-color-secondarycontainer);
  padding: 12px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  font-size: 13px;
  .label { color: var(--td-text-color-secondary); min-width: 70px; }
  .value { font-weight: 500; word-break: break-all; color: var(--td-text-color-primary); }
}
.form-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  .label { font-size: 14px; font-weight: 500; color: var(--td-text-color-primary); }
}
.switch-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 0;
  .switch-info {
    display: flex;
    flex-direction: column;
    gap: 4px;
    .switch-label { font-size: 14px; font-weight: 500; }
    .switch-tip { font-size: 12px; color: var(--td-text-color-placeholder); }
  }
}
.progress-area {
  padding: 10px 0;
  .status-header {
    display: flex;
    justify-content: space-between;
    margin-bottom: 8px;
    font-size: 13px;
    color: var(--td-brand-color);
  }
}
.actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 10px;
}
</style>
