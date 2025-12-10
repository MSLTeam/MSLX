<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon } from 'tdesign-icons-vue-next';
import { getCompressStatus, startCompress } from '@/api/files';

const props = defineProps<{
  visible: boolean;
  instanceId: number;
  currentPath: string;
  files: string[]; // 要压缩的文件名列表
}>();

const emit = defineEmits(['update:visible', 'success']);

const targetName = ref('');
const status = ref<'idle' | 'processing' | 'success' | 'error'>('idle');
const progress = ref(0);
const statusMsg = ref('');
let pollTimer: number | null = null;

// 初始化
watch(
  () => props.visible,
  (val) => {
    if (val) {
      status.value = 'idle';
      progress.value = 0;
      statusMsg.value = '';
      // 默认文件名
      if (props.files.length > 0) {
        targetName.value = `${props.files[0]}_packed.zip`;
      }
    } else {
      stopPolling();
    }
  },
);

const stopPolling = () => {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
};

const handleStart = async () => {
  if (!targetName.value.trim()) {
    MessagePlugin.warning('请输入压缩包名称');
    return;
  }

  // 补全后缀
  let finalName = targetName.value;
  if (!finalName.endsWith('.zip')) finalName += '.zip';

  status.value = 'processing';
  progress.value = 0;
  statusMsg.value = '正在提交任务...';

  try {
    const res = await startCompress(props.instanceId, props.files, finalName, props.currentPath);
    const taskId = res.taskId;

    if (taskId) {
      pollProgress(taskId);
    } else {
      throw new Error('未获取到任务ID');
    }
  } catch (err: any) {
    status.value = 'error';
    statusMsg.value = err.message || '提交失败';
  }
};

const pollProgress = (taskId: string) => {
  pollTimer = window.setInterval(async () => {
    try {
      const data = await getCompressStatus(taskId);

      progress.value = data.progress;
      statusMsg.value = data.message;

      if (data.status === 'success') {
        stopPolling();
        status.value = 'success';
        progress.value = 100;
        setTimeout(() => {
          emit('success');
          emit('update:visible', false);
        }, 1000);
      } else if (data.status === 'error') {
        stopPolling();
        status.value = 'error';
      }
    } catch (err) {
      console.error(err);
    }
  }, 1000); // 1秒轮询一次
};

const handleClose = () => {
  if (status.value === 'processing') {
    MessagePlugin.warning('后台任务仍在进行中，关闭窗口不会取消任务');
  }
  stopPolling();
  emit('update:visible', false);
};

onUnmounted(() => stopPolling());
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="status === 'idle' ? '创建压缩包' : '正在压缩'"
    :footer="false"
    :close-on-overlay-click="false"
    width="480px"
    @close="handleClose"
  >
    <div v-if="status === 'idle'" class="compress-form">
      <div class="file-list-preview">
        即将压缩 <span>{{ files.length }}</span> 个文件/文件夹
      </div>
      <t-input v-model="targetName" placeholder="请输入文件名" suffix=".zip" autofocus @enter="handleStart" />
      <div class="actions">
        <t-button theme="default" variant="text" @click="handleClose">取消</t-button>
        <t-button theme="primary" @click="handleStart">开始压缩</t-button>
      </div>
    </div>

    <div v-else class="progress-view">
      <div class="status-icon">
        <t-loading v-if="status === 'processing'" />
        <check-circle-filled-icon
          v-else-if="status === 'success'"
          style="color: var(--td-success-color); font-size: 40px"
        />
        <error-circle-filled-icon
          v-else-if="status === 'error'"
          style="color: var(--td-error-color); font-size: 40px"
        />
      </div>

      <div class="status-text">{{ statusMsg }}</div>

      <t-progress
        theme="plump"
        :percentage="progress"
        :status="status === 'error' ? 'error' : status === 'success' ? 'success' : 'active'"
      />
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.compress-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
  .file-list-preview {
    color: var(--td-text-color-secondary);
    font-size: 13px;
    span {
      font-weight: bold;
      color: var(--td-brand-color);
    }
  }
  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 8px;
  }
}

.progress-view {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  padding: 12px 0;
  .status-text {
    color: var(--td-text-color-primary);
    font-weight: 500;
  }
  .t-progress {
    width: 100%;
  }
}
</style>
