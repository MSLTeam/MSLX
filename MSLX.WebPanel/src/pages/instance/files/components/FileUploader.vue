<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue';
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
import { finishUpload, initUpload, saveUploadedFile, uploadChunk } from '@/api/files';

const props = withDefaults(
  defineProps<{
    visible: boolean;
    instanceId: number;
    currentPath: string;
    allowFolder?: boolean;
  }>(),
  {
    allowFolder: true,
  },
);

const emit = defineEmits(['update:visible', 'success']);

interface UploadTask {
  id: string;
  file: File;
  path: string;
  status: 'pending' | 'uploading' | 'merging' | 'success' | 'error';
  progress: number;
  speed: string;
  errorMsg?: string;
  abortController?: AbortController;
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

  // 默认图标
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

const addTasksToQueue = (items: { file: File; path: string }[]) => {
  items.forEach(({ file, path }) => {
    if (tasks.value.some((t) => t.path === path && t.file.size === file.size && t.status !== 'error')) return;
    tasks.value.push({
      id: Math.random().toString(36).substring(2),
      file,
      path,
      status: 'pending',
      progress: 0,
      speed: '',
    });
  });
};

const processQueue = async () => {
  if (isUploading.value) return;
  isUploading.value = true;
  isCancelAction.value = false;
  const CONCURRENCY = 3;
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

const uploadSingleFile = async (task: UploadTask) => {
  task.status = 'uploading';
  task.progress = 0;
  task.abortController = new AbortController();
  const startTime = Date.now();
  let lastUpdate = 0; // 用于节流 UI 更新

  try {
    const initRes = await initUpload();
    const uploadId = initRes.uploadId;

    // 动态调整分片大小
    const isLargeFile = task.file.size > 200 * 1024 * 1024;
    const CHUNK_SIZE = isLargeFile ? 50 * 1024 * 1024 : 10 * 1024 * 1024;
    const totalChunks = Math.ceil(task.file.size / CHUNK_SIZE);

    // 并发与重试配置
    const maxConcurrency = 4;
    const maxRetries = 5;

    // 任务队列
    const chunkIndices = Array.from({ length: totalChunks }, (_, i) => i);

    // 分片已上传的字节
    const chunkProgressMap = new Map<number, number>();

    // 更新进度和速度
    const updateProgress = () => {
      const now = Date.now();
      // 100ms/once
      if (now - lastUpdate < 100) return;
      lastUpdate = now;

      // 计算所有分片已上传的总字节数
      const loadedBytes = Array.from(chunkProgressMap.values()).reduce((sum, val) => sum + val, 0);

      // 计算百分比  剩下5%是合并
      const percent = Math.min((loadedBytes / task.file.size) * 95, 95);
      task.progress = Number(percent.toFixed(1));

      // 计算速度
      const elapsed = (now - startTime) / 1000;
      if (elapsed > 0) {
        const speedVal = loadedBytes / 1024 / 1024 / elapsed;
        task.speed = speedVal.toFixed(1) + ' MB/s';
      }
    };

    // 单个分片处理逻辑
    const processChunk = async (index: number) => {
      if (task.abortController?.signal.aborted) throw new Error('已取消');

      const start = index * CHUNK_SIZE;
      const end = Math.min(task.file.size, start + CHUNK_SIZE);
      const chunk = task.file.slice(start, end);

      let lastError: any;

      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        if (task.abortController?.signal.aborted) throw new Error('已取消');

        try {
          // 回调单个进度
          await uploadChunk(
            uploadId,
            index,
            chunk,
            (e: any) => {
              if (e && e.loaded) {
                chunkProgressMap.set(index, e.loaded);
                updateProgress(); // 触发 UI 更新
              }
            },
            task.abortController?.signal,
          );

          // 确保完整进度
          chunkProgressMap.set(index, chunk.size);
          updateProgress();
          return;
        } catch (err: any) {
          lastError = err;
          if (attempt < maxRetries) {
            // 失败重试 reset掉
            chunkProgressMap.set(index, 0);
            updateProgress();
            await new Promise((r) => setTimeout(r, 1000 * attempt));
          }
        }
      }
      throw lastError || new Error(`分片 ${index} 失败`);
    };

    // Worker 消费者
    const worker = async () => {
      while (chunkIndices.length > 0) {
        if (task.abortController?.signal.aborted) break;
        const index = chunkIndices.shift();
        if (index === undefined) break;
        await processChunk(index);
      }
    };

    // 启动并发
    const workers = Array(Math.min(maxConcurrency, totalChunks))
      .fill(null)
      .map(() => worker());

    await Promise.all(workers);

    if (task.abortController.signal.aborted) throw new Error('已取消');

    // 合并阶段
    task.status = 'merging';
    task.speed = '合并中...';
    // 此时已经是 95% 以上了，稍微再推一下
    task.progress = 98;

    await finishUpload(uploadId, totalChunks);
    await saveUploadedFile(props.instanceId, uploadId, task.path, props.currentPath);

    task.status = 'success';
    task.progress = 100;
    task.speed = '完成';
  } catch (err: any) {
    if (err.message === '已取消' || task.abortController?.signal.aborted) {
      task.status = 'pending';
      task.speed = '已取消';
      task.progress = 0;
    } else {
      task.status = 'error';
      task.errorMsg = err.message || '失败';
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
  <t-dialog attach="body" :visible="visible" header="批量上传文件" width="650px" :footer="false" @close="handleClose">

    <input ref="fileInputRef" type="file" multiple class="hidden" @change="onFileChange" />
    <input ref="folderInputRef" type="file" webkitdirectory class="hidden" @change="onFileChange" />

    <div class="flex flex-col gap-4 max-h-[60vh]">

      <div
        class="border-2 border-dashed rounded-xl p-6 flex flex-col items-center gap-3 transition-all duration-300"
        :class="dragOver ? 'border-[var(--color-primary)] bg-[var(--color-primary)]/5 scale-[0.99]' : 'border-zinc-200 dark:border-zinc-700 bg-zinc-50 dark:bg-zinc-800/40 hover:border-zinc-300 dark:hover:border-zinc-600'"
        @dragover.prevent="dragOver = true"
        @dragleave.prevent="dragOver = false"
        @drop.prevent="handleDrop"
      >
        <cloud-upload-icon size="40px" class="text-[var(--color-primary)]" />
        <p class="text-[13px] font-medium text-zinc-500 dark:text-zinc-400 m-0">
          {{ props.allowFolder ? '拖入文件或文件夹至此处' : '拖入文件至此处' }}
        </p>
        <div class="flex gap-3 mt-1">
          <t-button variant="outline" size="small" class="!rounded-lg !bg-white dark:!bg-zinc-900 !border-zinc-200 dark:!border-zinc-700" @click="handleSelectFiles">
            <template #icon><file-icon /></template> 选择文件
          </t-button>
          <t-button v-if="props.allowFolder" variant="outline" size="small" class="!rounded-lg !bg-white dark:!bg-zinc-900 !border-zinc-200 dark:!border-zinc-700" @click="handleSelectFolder">
            <template #icon><folder-icon /></template> 选择文件夹
          </t-button>
        </div>
      </div>

      <div v-if="tasks.length > 0" class="flex justify-between items-center pb-2 border-b border-zinc-200 dark:border-zinc-700/60">
        <span class="text-xs text-zinc-500 dark:text-zinc-400 font-medium">
          队列: <span class="text-zinc-800 dark:text-zinc-200 font-bold mx-0.5">{{ tasks.length }}</span> 个
          <template v-if="isUploading">
            <span class="mx-1.5 opacity-50">|</span> 总进度 <span class="text-[var(--color-primary)] font-bold ml-0.5">{{ totalProgress }}%</span>
          </template>
        </span>
        <div class="flex gap-2">
          <t-button theme="primary" size="small" class="!rounded-md shadow-sm" :disabled="!hasPending || isUploading" @click="processQueue">
            <template #icon><play-circle-icon /></template> {{ isUploading ? '上传中...' : '开始上传' }}
          </t-button>
          <t-button variant="text" size="small" class="!rounded-md hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !text-zinc-500" @click="clearFinished">
            <template #icon><clear-icon /></template> 清空已完成
          </t-button>
        </div>
      </div>

      <div v-if="tasks.length > 0" class="flex-1 overflow-y-auto flex flex-col gap-2 pr-1 custom-scrollbar">
        <div v-for="(task, index) in tasks" :key="task.id" class="flex items-start gap-3 p-3 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-100 dark:border-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600 transition-colors group">

          <div class="pt-0.5 shrink-0 text-xl">
            <component
              :is="getTaskIcon(getFileName(task.path)).icon"
              :style="{ color: getTaskIcon(getFileName(task.path)).color }"
            />
          </div>

          <div class="flex-1 overflow-hidden flex flex-col gap-1">
            <div class="flex justify-between items-center text-[13px]">
              <div class="font-medium text-zinc-800 dark:text-zinc-200 truncate max-w-[200px] sm:max-w-[280px]" :title="getFileName(task.path)">
                {{ getFileName(task.path) }}
              </div>
              <div class="text-[11px] text-zinc-400 dark:text-zinc-500 flex items-center gap-2 font-mono">
                <span v-if="task.status === 'error'" class="text-red-500 font-sans font-medium">{{ task.errorMsg }}</span>
                <span v-else>{{ task.speed }}</span>
                <span class="bg-zinc-200/50 dark:bg-zinc-700/50 px-1.5 py-0.5 rounded">{{ (task.file.size / 1024 / 1024).toFixed(2) }} MB</span>
              </div>
            </div>

            <div v-if="getDirectory(task.path)" class="text-[11px] text-zinc-500 dark:text-zinc-400 flex items-center gap-1 truncate" :title="getDirectory(task.path)">
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
