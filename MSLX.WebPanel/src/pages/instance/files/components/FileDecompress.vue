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
    <div class="flex flex-col gap-5 py-2">

      <div class="bg-zinc-50 dark:bg-zinc-800/40 p-3 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 flex items-start text-[13px] shadow-inner">
        <span class="text-[var(--td-text-color-secondary)] min-w-[70px] shrink-0 pt-0.5">目标文件：</span>
        <span class="font-medium font-mono text-[var(--td-text-color-primary)] break-all leading-relaxed">{{ fileName }}</span>
      </div>

      <div class="flex flex-col gap-2" v-if="!isProcessing">
        <span class="text-sm font-medium text-[var(--td-text-color-primary)]">文件名编码</span>
        <t-select v-model="encoding" :options="encodingOptions" class="!rounded-lg shadow-sm" />
      </div>

      <div class="flex justify-between items-center py-1" v-if="!isProcessing">
        <div class="flex flex-col gap-1 pr-4">
          <span class="text-sm font-medium text-[var(--td-text-color-primary)]">创建同名文件夹</span>
          <span class="text-xs text-[var(--td-text-color-secondary)]">推荐开启，防止文件散乱在当前目录</span>
        </div>
        <t-switch v-model="createSubFolder" size="large" class="shrink-0" />
      </div>

      <div class="py-2" v-if="isProcessing">
        <div class="flex justify-between items-center mb-2 text-[13px] text-[var(--color-primary)] font-medium">
          <span class="truncate pr-4">{{ statusMessage }}</span>
          <span class="font-mono font-bold shrink-0">{{ progress }}%</span>
        </div>
        <t-progress
          theme="line"
          :percentage="progress"
          :label="false"
          :status="progress === 100 ? 'success' : 'active'"
        />
      </div>

      <div class="flex justify-end gap-3 mt-2" v-if="!isProcessing">
        <t-button variant="outline" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose">取消</t-button>
        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleStart">开始解压</t-button>
      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
