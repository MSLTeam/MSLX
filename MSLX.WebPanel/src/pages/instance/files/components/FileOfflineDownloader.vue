<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon } from 'tdesign-icons-vue-next';
import { addOfflineDownloadTask, getOfflineDownloadTaskStatus } from '@/api/files';

const props = defineProps<{
  visible: boolean;
  instanceId: number;
  currentPath: string;
}>();

const emit = defineEmits(['update:visible', 'success']);

const url = ref('');
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
      url.value = '';
      targetName.value = '';
    } else {
      stopPolling();
    }
  },
);

// 尝试获取文件名
watch(url, (newVal) => {
  if (newVal && !targetName.value.trim()) {
    try {
      const urlObj = new URL(newVal);
      const pathname = urlObj.pathname;
      const lastSegment = pathname.split('/').filter(Boolean).pop();
      if (lastSegment) {
        targetName.value = decodeURIComponent(lastSegment);
      }
    } catch  {
      const cleanUrl = newVal.split('?')[0];
      const lastSegment = cleanUrl.split('/').filter(Boolean).pop();
      if (lastSegment && newVal.includes('/')) {
        targetName.value = decodeURIComponent(lastSegment);
      }
    }
  }
});

const stopPolling = () => {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
};

const handleStart = async () => {
  if (!url.value.trim()) {
    MessagePlugin.warning('请输入下载链接');
    return;
  }

  status.value = 'processing';
  progress.value = 0;
  statusMsg.value = '正在提交离线下载任务...';

  try {
    const res = await addOfflineDownloadTask(
      props.instanceId,
      props.currentPath,
      url.value.trim(),
      targetName.value.trim(),
    );
    const taskId = res.taskId || (res.data && res.data.taskId); // 兼容可能包裹在 data 里的情况

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
      const statusData = await getOfflineDownloadTaskStatus(taskId);

      progress.value = statusData.progress;
      statusMsg.value = statusData.message;

      if (statusData.status === 'success') {
        stopPolling();
        status.value = 'success';
        progress.value = 100;
        setTimeout(() => {
          emit('success');
          emit('update:visible', false);
        }, 1000);
      } else if (statusData.status === 'error') {
        stopPolling();
        status.value = 'error';
      }
    } catch (err) {
      console.error(err);
    }
  }, 1000); // 1秒
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
    :header="status === 'idle' ? '创建离线下载任务' : '正在下载'"
    :footer="false"
    :close-on-overlay-click="false"
    width="480px"
    @close="handleClose"
  >
    <div v-if="status === 'idle'" class="flex flex-col gap-4 py-2">
      <div
        class="bg-zinc-50 dark:bg-zinc-800/40 p-3 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 text-[13px] text-[var(--td-text-color-secondary)] shadow-inner"
      >
        文件将下载至当前目录:
        <span class="font-bold font-mono text-[var(--color-primary)]">{{ currentPath || '根目录' }}</span>
      </div>

      <t-input v-model="url" placeholder="请输入直链 URL (必填)" autofocus class="!rounded-lg shadow-sm" clearable />

      <t-input
        v-model="targetName"
        placeholder="保存的文件名 (选填，默认从链接推断)"
        class="!rounded-lg shadow-sm"
        @enter="handleStart"
      />

      <div class="flex justify-end gap-3 mt-2">
        <t-button variant="outline" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose"
          >取消</t-button
        >
        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleStart">开始下载</t-button>
      </div>
    </div>

    <div v-else class="flex flex-col items-center gap-4 py-4 w-full">
      <div class="flex justify-center items-center h-10">
        <t-loading v-if="status === 'processing'" size="medium" />
        <check-circle-filled-icon v-else-if="status === 'success'" class="text-emerald-500 text-[40px]" />
        <error-circle-filled-icon v-else-if="status === 'error'" class="text-red-500 text-[40px]" />
      </div>

      <div class="text-sm font-medium text-[var(--td-text-color-primary)] text-center px-4 w-full truncate">
        {{ statusMsg }}
      </div>

      <div class="w-full">
        <t-progress
          theme="plump"
          :percentage="progress"
          :status="status === 'error' ? 'error' : status === 'success' ? 'success' : 'active'"
        />
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
