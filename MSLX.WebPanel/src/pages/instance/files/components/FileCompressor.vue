<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { useTaskStore } from '@/store';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon } from 'tdesign-icons-vue-next';
import { getCompressStatus, startCompress } from '@/api/files';

const props = defineProps<{
  visible: boolean;
  instanceId: number;
  currentPath: string;
  files: string[];
}>();

const emit = defineEmits(['update:visible', 'success']);

const formatOptions = [
  { label: '.zip (标准格式，兼容性最好)', value: '.zip' },
  { label: '.tar.gz (Linux常用，高压缩率)', value: '.tar.gz' },
  { label: '.tar (仅打包，速度极快)', value: '.tar' },
  { label: '.tar.bz2 (高压缩比)', value: '.tar.bz2' },
  { label: '.7z (极限压缩比)', value: '.7z' },
];

const targetName = ref('');
const selectedFormat = ref('.zip');
const enablePassword = ref(false);
const password = ref('');
const status = ref<'idle' | 'processing' | 'success' | 'error'>('idle');
const progress = ref(0);
const statusMsg = ref('');
let pollTimer: number | null = null;

// Tar 系列格式不支持原生包内密码
const supportsPassword = computed(() => {
  return selectedFormat.value === '.zip' || selectedFormat.value === '.7z';
});
watch(selectedFormat, (val) => {
  if (val !== '.zip' && val !== '.7z') {
    enablePassword.value = false;
    password.value = '';
  }
});

// 初始化
watch(
  () => props.visible,
  (val) => {
    if (val) {
      status.value = 'idle';
      progress.value = 0;
      statusMsg.value = '';
      selectedFormat.value = '.zip';
      enablePassword.value = false;
      password.value = '';
      // 默认文件名
      if (props.files.length > 0) {
        const first = props.files[0];
        const base = first.replace(/\.[^/.]+$/, '');
        targetName.value = `${base || 'archive'}_packed`;
      } else {
        targetName.value = 'archive_packed';
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
  const name = targetName.value.trim();
  if (!name) {
    MessagePlugin.warning('请输入压缩包名称');
    return;
  }

  if (enablePassword.value && !password.value) {
    MessagePlugin.warning('已开启密码保护，请输入密码');
    return;
  }

  // 检查是否已有合法扩展名
  const hasExt = /\.(zip|tar|tar\.gz|tgz|tar\.bz2|tbz2|tar\.xz|txz|7z)$/i.test(name);
  const finalName = hasExt ? name : `${name}${selectedFormat.value}`;
  const finalPassword = enablePassword.value ? password.value : undefined;

  status.value = 'processing';
  progress.value = 0;
  statusMsg.value = '正在提交任务...';

  try {
    const res = await startCompress(props.instanceId, props.files, finalName, props.currentPath, finalPassword);
    const taskId = res.taskId;

    if (taskId) {
      pollProgress(taskId);
      useTaskStore().fetchTasks();
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
    MessagePlugin.warning('任务已转入后台，可随时在右上角任务中心查看');
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

      <div class="flex flex-col gap-1.5">
        <span class="text-xs font-medium text-[var(--td-text-color-secondary)]">压缩格式</span>
        <t-select
          v-model="selectedFormat"
          :options="formatOptions"
          class="!rounded-lg shadow-sm"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <span class="text-xs font-medium text-[var(--td-text-color-secondary)]">文件名</span>
        <t-input
          v-model="targetName"
          placeholder="请输入文件名"
          :suffix="selectedFormat"
          autofocus
          class="!rounded-lg shadow-sm"
          @enter="handleStart"
        />
      </div>

      <div v-if="supportsPassword" class="flex justify-between items-center py-1">
        <div class="flex flex-col gap-0.5">
          <span class="text-sm font-medium text-[var(--td-text-color-primary)]">设置密码保护</span>
          <span class="text-xs text-[var(--td-text-color-secondary)]">开启后解压文件需要输入密码</span>
        </div>
        <t-switch v-model="enablePassword" />
      </div>

      <div v-if="supportsPassword && enablePassword" class="flex flex-col gap-1.5">
        <span class="text-xs font-medium text-[var(--td-text-color-secondary)]">设置密码</span>
        <t-input
          v-model="password"
          type="password"
          placeholder="请输入压缩保护密码"
          class="!rounded-lg shadow-sm"
          clearable
          @enter="handleStart"
        />
      </div>

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

      <div class="flex justify-center mt-2 w-full" v-if="status === 'processing'">
        <t-button variant="outline" size="small" class="!rounded-lg" @click="handleClose">
          转入后台运行
        </t-button>
      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
