<script setup lang="ts">
import { getPageNodeId } from '@/utils/nodeTarget';
const targetNodeId = getPageNodeId();
import { confirmNodeAction } from '@/utils/nodeDialog';
import { computed, onUnmounted, ref, watch } from 'vue';
import {
  CheckCircleFilledIcon,
  ClearIcon,
  CloseCircleIcon,
  CloudUploadIcon,
  CodeIcon,
  FileIcon,
  FileImageIcon,
  FilePasteIcon,
  FileZipIcon,
  FolderIcon,
  PlayCircleIcon,
  ServiceIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  getUploadStatus,
  saveUploadedFile,
} from '@/api/files';
import {
  deleteUploadResumeRecord,
  getUploadResumeRecords,
} from '@/utils/uploadResume';
import { uploadFileInChunks } from '@/utils/chunkedUpload';
import type { UploadResumeRecord } from '@/utils/uploadResume';

const props = withDefaults(
  defineProps<{
    visible: boolean;
    instanceId?: number;
    currentPath?: string;
    initialItems?: Array<{ file: File; path: string }>;
    existingTopLevelNames?: string[];
    allowFolder?: boolean;
    title?: string;
    accept?: string;
    customUploadHandler?: (_uploadId: string, _task: UploadTask) => Promise<void>;
  }>(),
  {
    allowFolder: true,
    title: '批量上传文件',
    accept: '',
  },
);

const emit = defineEmits(['update:visible', 'success']);

interface UploadTask {
  nodeId?: string;
  id: string;
  file: File;
  path: string;
  status: 'pending' | 'uploading' | 'merging' | 'success' | 'error';
  progress: number; // UI 显示进度
  speed: string;
  errorMsg?: string;
  abortController?: AbortController;
  committedBytes: number; // 已确认上传的字节
  uploadId?: string;
  resumeRecord?: UploadResumeRecord;
}

const tasks = ref<UploadTask[]>([]);
const isUploading = ref(false);
const isCancelAction = ref(false);
const dragOver = ref(false);
const fileInputRef = ref<HTMLInputElement>();
const folderInputRef = ref<HTMLInputElement>();

// 文件名获取图标
const getTaskIcon = (filename: string) => {
  const ext = filename.split('.').pop()?.toLowerCase();

  if (['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp'].includes(ext || '')) {
    return { icon: FileImageIcon, color: 'var(--td-success-color)' };
  }
  if (['jar', 'zip', 'rar', '7z', 'tar', 'gz'].includes(ext || '')) {
    return { icon: FileZipIcon, color: '#722ed1' };
  }
  if (
    [
      'js',
      'ts',
      'py',
      'java',
      'c',
      'cpp',
      'cs',
      'json',
      'yml',
      'yaml',
      'xml',
      'html',
      'css',
      'properties',
      'conf',
      'sh',
      'bat',
    ].includes(ext || '')
  ) {
    return { icon: CodeIcon, color: 'var(--td-warning-color)' };
  }
  if (['log', 'txt', 'md', 'lock'].includes(ext || '')) {
    return { icon: FilePasteIcon, color: 'var(--td-gray-color-6)' };
  }
  if (['db', 'db-wal', 'dat'].includes(ext || '')) {
    return { icon: ServiceIcon, color: 'var(--td-gray-color-8)' };
  }

  return { icon: FileIcon, color: 'var(--td-brand-color)' };
};

const getFileName = (path: string) => path.split('/').pop() || path;
const getDirectory = (path: string) => {
  const idx = path.lastIndexOf('/');
  return idx !== -1 ? path.substring(0, idx) : '';
};

const totalProgress = computed(() => {
  if (tasks.value.length === 0) return 0;
  const total = tasks.value.reduce((acc, task) => acc + task.progress, 0);
  return Math.floor(total / tasks.value.length);
});

const hasPending = computed(() => tasks.value.some((t) => t.status === 'pending' || t.status === 'error'));
const conflictTopLevelNames = computed(() => {
  const existingNames = new Set(props.existingTopLevelNames || []);
  return [...new Set(tasks.value.map((task) => task.path.split('/')[0]).filter((name) => existingNames.has(name)))];
});

const handleSelectFiles = () => fileInputRef.value?.click();
const handleSelectFolder = () => folderInputRef.value?.click();

const onFileChange = (e: Event) => {
  const input = e.target as HTMLInputElement;
  if (input.files && input.files.length > 0) {
    const fileList = Array.from(input.files);
    const items = fileList.map((file) => ({
      file,
      path: file.webkitRelativePath || file.name,
    }));
    addTasksToQueue(items);
  }
  input.value = '';
};

const handleDrop = async (e: DragEvent) => {
  dragOver.value = false;
  const items = e.dataTransfer?.items;
  if (!items) return;

  const queueItems: { file: File; path: string }[] = [];
  const entries = Array.from(items)
    .map((item) => item.webkitGetAsEntry())
    .filter((entry) => entry !== null);

  for (const entry of entries) {
    if (entry) await traverseFileTree(entry, queueItems);
  }
  addTasksToQueue(queueItems);
};

const traverseFileTree = async (entry: any, queue: { file: File; path: string }[]) => {
  if (entry.isFile) {
    const file = await new Promise<File>((resolve, reject) => entry.file(resolve, reject));
    const relativePath = entry.fullPath.startsWith('/') ? entry.fullPath.slice(1) : entry.fullPath;
    queue.push({ file, path: relativePath });
  } else if (entry.isDirectory) {
    if (props.allowFolder) {
      const dirReader = entry.createReader();
      const entries = await readAllDirectoryEntries(dirReader);
      for (const subEntry of entries) {
        await traverseFileTree(subEntry, queue);
      }
    } else {
      MessagePlugin.error('此处不支持上传文件夹');
    }
  }
};

const readAllDirectoryEntries = async (dirReader: any) => {
  let entries: any[] = [];
  const read = async () => {
    const result = await new Promise<any[]>((resolve, reject) => dirReader.readEntries(resolve, reject));
    if (result.length > 0) {
      entries = entries.concat(result);
      await read();
    }
  };
  await read();
  return entries;
};

const addTasksToQueue = (items: { file: File; path: string }[], replace = false) => {
  if (replace) tasks.value = [];
  items.forEach(({ file, path }) => {
    if (tasks.value.some((t) => t.path === path && t.file.size === file.size && t.status !== 'error')) return;
    tasks.value.push({
      id: Math.random().toString(36).substring(2),
      file,
      path,
      status: 'pending',
      progress: 0,
      committedBytes: 0,
      speed: '',
    });
  });
};

const pendingResumeHintShown = ref(false);
const checkPendingResumes = async () => {
  if (pendingResumeHintShown.value || tasks.value.length > 0) return;
  const records = await getUploadResumeRecords(targetNodeId).catch(() => []);
  for (const record of records) {
    if (record.cancelledAt || (record.kind && record.kind !== 'file')) continue;
    try {
      const status = await getUploadStatus(record.uploadId, record.nodeId);
      if ((status.receivedChunks || []).length === 0 && record.receivedChunks.length === 0) {
        // 会话已创建但还没有分片，也仍然允许用户重新选择后复用。
      }
      pendingResumeHintShown.value = true;
      MessagePlugin.info(`检测到未完成的文件上传，请重新选择 ${record.fileName} 继续上传`);
      return;
    } catch (error) {
      const status = getHttpStatus(error);
      if (status === 403 || status === 404) {
        await deleteUploadResumeRecord(record.key).catch(() => {});
      }
    }
  }
};

watch(
  () => [props.visible, props.initialItems] as const,
  ([visible, initialItems]) => {
    if (visible) {
      if (initialItems?.length) addTasksToQueue(initialItems, true);
      void checkPendingResumes();
    } else {
      pendingResumeHintShown.value = false;
    }
  },
);

const handleStartUpload = () => {
  if (isUploading.value || !hasPending.value) return;
  if (conflictTopLevelNames.value.length === 0) {
    processQueue();
    return;
  }

  const conflicts = conflictTopLevelNames.value;
  const displayNames = conflicts.slice(0, 10).join('、');
  const moreText = conflicts.length > 10 ? ` 等 ${conflicts.length} 项` : '';
  const confirmDialog = confirmNodeAction({
      header: '确认覆盖上传',
      body: `检测到 ${conflicts.length} 个同名顶层目标：${displayNames}${moreText}。继续上传将覆盖原文件，是否继续？`,
      theme: 'warning',
      onConfirm: () => {
        confirmDialog.hide();
        processQueue();
      },
    });
};

const processQueue = async () => {
  if (isUploading.value) return;
  isUploading.value = true;
  isCancelAction.value = false;
  const CONCURRENCY = 3; // “文件级”并发
  const pendingTasks = tasks.value.filter((t) => t.status === 'pending' || t.status === 'error');

  for (let i = 0; i < pendingTasks.length; i += CONCURRENCY) {
    if (isCancelAction.value && tasks.value.length === 0) break;

    const batch = pendingTasks.slice(i, i + CONCURRENCY);
    const activeBatch = batch.filter((t) => tasks.value.some((item) => item.id === t.id));

    if (activeBatch.length > 0) {
      await Promise.all(activeBatch.map((task) => uploadSingleFile(task)));
    }
  }

  isUploading.value = false;
  if (!isCancelAction.value && tasks.value.length > 0 && tasks.value.every((t) => t.status === 'success')) {
    MessagePlugin.success('上传完成');
    emit('success');
  }
};

const getHttpStatus = (error: any): number | undefined => {
  const responseCode = error?.responseData?.code ?? error?.response?.data?.code;
  return typeof responseCode === 'number' ? responseCode : error?.status ?? error?.response?.status;
};

const uploadSingleFile = async (task: UploadTask) => {
  task.nodeId = targetNodeId;
  task.status = 'uploading';
  task.progress = 0;
  task.committedBytes = 0;
  task.abortController = new AbortController();

  try {
    const result = await uploadFileInChunks(task.file, {
      nodeId: targetNodeId,
      signal: task.abortController.signal,
      kind: 'file',
      resumeScope: JSON.stringify([props.instanceId ?? null, props.currentPath ?? '', task.path]),
      progressMax: 95,
      onSession: (record) => {
        task.uploadId = record.uploadId;
        task.resumeRecord = record;
      },
      onProgress: (progress, speedMBps) => {
        task.progress = Math.max(task.progress, progress);
        task.speed = speedMBps > 0 ? `${speedMBps.toFixed(1)} MB/s` : task.speed;
      },
      onStatus: (status) => {
        if (status === 'merging') {
          task.status = 'merging';
          task.speed = '合并中...';
          task.progress = 98;
        }
      },
    });

    if (props.customUploadHandler) {
      await props.customUploadHandler(result.uploadId, task);
    } else if (props.instanceId !== undefined && props.currentPath !== undefined) {
      await saveUploadedFile(props.instanceId, result.uploadId, task.path, props.currentPath);
    }

    task.status = 'success';
    task.progress = 100;
    task.speed = '完成';
  } catch (err: any) {
    if (err?.message === '已取消' || task.abortController?.signal.aborted) {
      task.status = 'pending';
      task.speed = '已取消';
      task.progress = 0;
    } else {
      task.status = 'error';
      task.errorMsg = err?.message || '失败';
    }
  }
};

const removeTask = (index: number) => {
  const task = tasks.value[index];
  task.abortController?.abort();
  tasks.value.splice(index, 1);
};

const clearFinished = () => {
  tasks.value = tasks.value.filter((t) => t.status !== 'success');
};

const handleClose = () => emit('update:visible', false);
onUnmounted(() => tasks.value.forEach((t) => t.abortController?.abort()));
</script>

<template>
  <t-dialog attach="body" :visible="visible" :header="props.title" width="650px" :footer="false" @close="handleClose">
    <input ref="fileInputRef" type="file" multiple class="hidden" :accept="props.accept" @change="onFileChange" />
    <input ref="folderInputRef" type="file" webkitdirectory class="hidden" @change="onFileChange" />

    <div class="flex flex-col gap-4 max-h-[60vh]">
      <div
        class="border-2 border-dashed rounded-xl p-6 flex flex-col items-center gap-3 transition-all duration-300"
        :class="
          dragOver
            ? 'border-[var(--color-primary)] bg-[var(--color-primary)]/5 scale-[0.99]'
            : 'border-zinc-200 dark:border-zinc-700 bg-zinc-50 dark:bg-zinc-800/40 hover:border-zinc-300 dark:hover:border-zinc-600'
        "
        @dragover.prevent="dragOver = true"
        @dragleave.prevent="dragOver = false"
        @drop.prevent="handleDrop"
      >
        <cloud-upload-icon size="40px" class="text-[var(--color-primary)]" />
        <p class="text-[13px] font-medium text-[var(--td-text-color-secondary)] m-0">
          {{ props.allowFolder ? '拖入文件或文件夹至此处' : '拖入文件至此处' }}
        </p>
        <div class="flex gap-3 mt-1">
          <t-button
            variant="outline"
            size="small"
            class="!rounded-lg !bg-white dark:!bg-zinc-900 !border-zinc-200 dark:!border-zinc-700"
            @click="handleSelectFiles"
          >
            <template #icon><file-icon /></template> 选择文件
          </t-button>
          <t-button
            v-if="props.allowFolder"
            variant="outline"
            size="small"
            class="!rounded-lg !bg-white dark:!bg-zinc-900 !border-zinc-200 dark:!border-zinc-700"
            @click="handleSelectFolder"
          >
            <template #icon><folder-icon /></template> 选择文件夹
          </t-button>
        </div>
      </div>

      <div
        v-if="tasks.length > 0"
        class="flex justify-between items-center pb-2 border-b border-zinc-200 dark:border-zinc-700/60"
      >
        <span class="text-xs text-[var(--td-text-color-secondary)] font-medium">
          队列: <span class="text-[var(--td-text-color-primary)] font-bold mx-0.5">{{ tasks.length }}</span> 个
          <template v-if="isUploading">
            <span class="mx-1.5 opacity-50">|</span> 总进度
            <span class="text-[var(--color-primary)] font-bold ml-0.5">{{ totalProgress }}%</span>
          </template>
        </span>
        <div class="flex gap-2">
          <t-button
            theme="primary"
            size="small"
            class="!rounded-md shadow-sm"
            :disabled="!hasPending || isUploading"
            @click="handleStartUpload"
          >
            <template #icon><play-circle-icon /></template> {{ isUploading ? '上传中...' : '开始上传' }}
          </t-button>
          <t-button
            variant="text"
            size="small"
            class="!rounded-md hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !text-zinc-500"
            @click="clearFinished"
          >
            <template #icon><clear-icon /></template> 清空已完成
          </t-button>
        </div>
      </div>

      <div v-if="tasks.length > 0" class="flex-1 overflow-y-auto flex flex-col gap-2 pr-1 custom-scrollbar">
        <div
          v-for="(task, index) in tasks"
          :key="task.id"
          class="flex items-start gap-3 p-3 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-100 dark:border-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600 transition-colors group"
        >
          <div class="pt-0.5 shrink-0 text-xl">
            <component
              :is="getTaskIcon(getFileName(task.path)).icon"
              :style="{ color: getTaskIcon(getFileName(task.path)).color }"
            />
          </div>

          <div class="flex-1 overflow-hidden flex flex-col gap-1">
            <div class="flex justify-between items-center text-[13px]">
              <div
                class="font-medium text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[280px]"
                :title="getFileName(task.path)"
              >
                {{ getFileName(task.path) }}
              </div>
              <div class="text-[11px] text-[var(--td-text-color-secondary)] flex items-center gap-2 font-mono">
                <span v-if="task.status === 'error'" class="text-red-500 font-sans font-medium">{{
                  task.errorMsg
                }}</span>
                <span v-else>{{ task.speed }}</span>
                <span class="bg-zinc-200/50 dark:bg-zinc-700/50 px-1.5 py-0.5 rounded"
                  >{{ (task.file.size / 1024 / 1024).toFixed(2) }} MB</span
                >
              </div>
            </div>

            <div
              v-if="getDirectory(task.path)"
              class="text-[11px] text-[var(--td-text-color-secondary)] flex items-center gap-1 truncate"
              :title="getDirectory(task.path)"
            >
              <folder-icon size="12px" class="shrink-0 opacity-70" /> {{ getDirectory(task.path) }}/
            </div>

            <div class="mt-0.5">
              <t-progress
                :percentage="task.progress"
                :status="task.status === 'error' ? 'error' : task.status === 'success' ? 'success' : 'active'"
                size="small"
                :label="false"
              />
            </div>
          </div>

          <div class="shrink-0 pt-0.5 flex items-center justify-center">
            <t-button
              v-if="task.status !== 'success'"
              shape="circle"
              variant="text"
              size="small"
              class="!text-zinc-400 hover:!text-red-500 hover:!bg-red-50 dark:hover:!bg-red-900/20 opacity-0 group-hover:opacity-100 transition-opacity"
              @click="removeTask(index)"
            >
              <close-circle-icon />
            </t-button>
            <check-circle-filled-icon v-else class="text-emerald-500 text-[18px]" />
          </div>
        </div>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 优雅的定制滚动条 */
.custom-scrollbar {
  scrollbar-width: thin;
  scrollbar-color: var(--td-scrollbar-color) transparent;

  &::-webkit-scrollbar {
    width: 4px;
  }
  &::-webkit-scrollbar-thumb {
    background: var(--td-scrollbar-color);
    border-radius: 2px;
  }
}
</style>
