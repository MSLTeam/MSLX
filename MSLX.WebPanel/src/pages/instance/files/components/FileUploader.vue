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
  <t-dialog :visible="visible" header="批量上传文件" width="650px" :footer="false" @close="handleClose">
    <input ref="fileInputRef" type="file" multiple style="display: none" @change="onFileChange" />
    <input ref="folderInputRef" type="file" webkitdirectory style="display: none" @change="onFileChange" />

    <div class="uploader-container">
      <div
        class="drop-zone"
        :class="{ active: dragOver }"
        @dragover.prevent="dragOver = true"
        @dragleave.prevent="dragOver = false"
        @drop.prevent="handleDrop"
      >
        <cloud-upload-icon size="40px" style="color: var(--td-brand-color)" />
        <p v-if="props.allowFolder" class="drop-text">拖入文件或文件夹</p>
        <p v-if="!props.allowFolder" class="drop-text">拖入文件</p>
        <div class="drop-actions">
          <t-button variant="outline" size="small" @click="handleSelectFiles"
            ><template #icon><file-icon /></template> 选择文件</t-button
          >
          <t-button v-if="props.allowFolder" variant="outline" size="small" @click="handleSelectFolder"
            ><template #icon><folder-icon /></template> 选择文件夹</t-button
          >
        </div>
      </div>

      <div v-if="tasks.length > 0" class="queue-actions">
        <span class="queue-info">
          队列: {{ tasks.length }} 个 | <span v-if="isUploading">总进度 {{ totalProgress }}%</span>
        </span>
        <div class="btn-group">
          <t-button theme="primary" size="small" :disabled="!hasPending || isUploading" @click="processQueue">
            <template #icon><play-circle-icon /></template> {{ isUploading ? '上传中...' : '开始上传' }}
          </t-button>
          <t-button variant="text" size="small" @click="clearFinished"
            ><template #icon><clear-icon /></template> 清空已完成</t-button
          >
        </div>
      </div>

      <div v-if="tasks.length > 0" class="file-list">
        <div v-for="(task, index) in tasks" :key="task.id" class="file-item">
          <div class="file-icon-wrapper">
            <component
              :is="getTaskIcon(getFileName(task.path)).icon"
              :style="{ color: getTaskIcon(getFileName(task.path)).color }"
            />
          </div>

          <div class="file-content">
            <div class="file-header">
              <div class="name" :title="getFileName(task.path)">{{ getFileName(task.path) }}</div>
              <div class="meta">
                <span v-if="task.status === 'error'" class="error">{{ task.errorMsg }}</span>
                <span v-else>{{ task.speed }}</span>
                <span class="size">{{ (task.file.size / 1024 / 1024).toFixed(2) }} MB</span>
              </div>
            </div>

            <div v-if="getDirectory(task.path)" class="file-path" :title="getDirectory(task.path)">
              <folder-icon size="10px" /> {{ getDirectory(task.path) }}/
            </div>

            <div class="progress-wrapper">
              <t-progress
                :percentage="task.progress"
                :status="task.status === 'error' ? 'error' : task.status === 'success' ? 'success' : 'active'"
                size="small"
                :label="false"
              />
            </div>
          </div>

          <div class="file-action">
            <t-button
              v-if="task.status !== 'success'"
              shape="circle"
              variant="text"
              size="small"
              @click="removeTask(index)"
            >
              <close-circle-icon />
            </t-button>
            <check-circle-filled-icon v-else style="color: var(--td-success-color); font-size: 18px" />
          </div>
        </div>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.uploader-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-height: 60vh;
}
.drop-zone {
  border: 2px dashed var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  padding: 20px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  background-color: var(--td-bg-color-container-hover);
  transition: all 0.3s;
  &.active {
    border-color: var(--td-brand-color);
    background-color: var(--td-brand-color-light);
  }
  .drop-text {
    color: var(--td-text-color-secondary);
    font-size: 13px;
    margin: 0;
  }
  .drop-actions {
    display: flex;
    gap: 12px;
  }
}
.queue-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--td-component-stroke);
  .queue-info {
    font-size: 12px;
    color: var(--td-text-color-placeholder);
  }
  .btn-group {
    display: flex;
    gap: 8px;
  }
}
.file-list {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-right: 4px;
  &::-webkit-scrollbar {
    width: 4px;
  }
  &::-webkit-scrollbar-thumb {
    background: var(--td-scrollbar-color);
    border-radius: 2px;
  }
}
.file-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 10px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);

  .file-icon-wrapper {
    padding-top: 2px;
    flex-shrink: 0;
    font-size: 18px;
  }

  .file-content {
    flex: 1;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    gap: 4px;

    .file-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 13px;
      .name {
        font-weight: 500;
        color: var(--td-text-color-primary);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 260px;
      }
      .meta {
        font-size: 11px;
        color: var(--td-text-color-placeholder);
        display: flex;
        gap: 8px;
        .error {
          color: var(--td-error-color);
        }
      }
    }

    .file-path {
      font-size: 11px;
      color: var(--td-text-color-secondary);
      display: flex;
      align-items: center;
      gap: 4px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .progress-wrapper {
      margin-top: 2px;
    }
  }

  .file-action {
    flex-shrink: 0;
    padding-top: 2px;
  }
}
</style>
