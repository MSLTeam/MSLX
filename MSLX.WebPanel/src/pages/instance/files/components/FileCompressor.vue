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
    <div v-if="status === 'idle'" class="flex flex-col gap-4 py-2">

      <div class="bg-zinc-50 dark:bg-zinc-800/40 p-3 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 text-[13px] text-[var(--td-text-color-secondary)] shadow-inner flex items-center">
        即将压缩
        <span class="font-bold font-mono text-[var(--color-primary)] mx-1.5 text-sm">{{ files.length }}</span>
        个文件/文件夹
      </div>

      <t-input
        v-model="targetName"
        placeholder="请输入文件名"
        suffix=".zip"
        autofocus
        class="!rounded-lg shadow-sm"
        @enter="handleStart"
      />

      <div class="flex justify-end gap-3 mt-2">
        <t-button variant="outline" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose">取消</t-button>
        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleStart">开始压缩</t-button>
      </div>

    </div>

    <div v-else class="flex flex-col items-center gap-4 py-4 w-full">

      <div class="flex justify-center items-center h-10">
        <t-loading v-if="status === 'processing'" size="medium" />
        <check-circle-filled-icon
          v-else-if="status === 'success'"
          class="text-emerald-500 text-[40px]"
        />
        <error-circle-filled-icon
          v-else-if="status === 'error'"
          class="text-red-500 text-[40px]"
        />
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
