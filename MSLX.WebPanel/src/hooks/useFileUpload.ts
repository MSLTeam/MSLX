import { ref, onUnmounted } from 'vue';
import { initUpload, uploadChunk, finishUpload, deleteUpload } from '@/api/files';

// 动态计算并发数
function calcConcurrency(speedMBps: number): number {
  if (speedMBps <= 0) return 2;
  if (speedMBps < 0.5) return 1;
  if (speedMBps < 2) return 2;
  return 4;
}

// 指数退避与抖动
function backoffDelay(attempt: number): Promise<void> {
  const base = Math.min(30_000, 500 * 2 ** attempt);
  const jitter = Math.random() * base * 0.3;
  return new Promise((r) => setTimeout(r, base + jitter));
}

export function useFileUpload() {
  const isUploading = ref(false);
  const uploadProgress = ref(0);
  const uploadedFileName = ref('');
  const uploadedFileSize = ref('');
  const fileKey = ref(''); // 成功后的文件 Key

  let abortController: AbortController | null = null;

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const startUpload = async (file: File): Promise<string> => {
    if (abortController) {
      abortController.abort();
    }
    abortController = new AbortController();

    isUploading.value = true;
    uploadProgress.value = 0;
    uploadedFileName.value = file.name;
    uploadedFileSize.value = formatFileSize(file.size);
    fileKey.value = '';

    let committedBytes = 0;
    let currentSpeedMBps = 0;
    const startTime = Date.now();

    const isLargeFile = file.size > 200 * 1024 * 1024;
    const chunkSize = isLargeFile ? 50 * 1024 * 1024 : 10 * 1024 * 1024;
    const totalChunks = Math.ceil(file.size / chunkSize);
    const maxRetries = 5;

    const chunkIndices = Array.from({ length: totalChunks }, (_, i) => i);
    const activeChunkProgressMap = new Map<number, number>();
    let lastUpdateTime = 0;

    const updateProgress = () => {
      const now = Date.now();
      if (now - lastUpdateTime < 100) return;
      lastUpdateTime = now;

      let activeLoadedBytes = 0;
      for (const loaded of activeChunkProgressMap.values()) {
        activeLoadedBytes += loaded;
      }
      const totalLoadedBytes = committedBytes + activeLoadedBytes;
      const percent = Math.min((totalLoadedBytes / file.size) * 98, 98);

      uploadProgress.value = Math.max(uploadProgress.value, Number(percent.toFixed(1)));

      const elapsed = (now - startTime) / 1000;
      if (elapsed > 0) {
        currentSpeedMBps = totalLoadedBytes / 1024 / 1024 / elapsed;
      }
    };

    try {
      const initRes = await initUpload();
      const uploadId = (initRes as any).uploadId;
      if (!uploadId) throw new Error('无法获取上传凭证');

      const processChunk = async (index: number) => {
        if (abortController?.signal.aborted) throw new Error('已取消');

        const start = index * chunkSize;
        const end = Math.min(file.size, start + chunkSize);
        const chunkBlob = file.slice(start, end);
        let lastError: any;

        for (let attempt = 1; attempt <= maxRetries; attempt++) {
          if (abortController?.signal.aborted) throw new Error('已取消');

          try {
            await uploadChunk(
              uploadId,
              index,
              chunkBlob,
              (e: any) => {
                if (e && e.loaded) {
                  activeChunkProgressMap.set(index, e.loaded);
                  updateProgress();
                }
              },
              abortController?.signal,
            );

            committedBytes += chunkBlob.size;
            activeChunkProgressMap.delete(index);
            updateProgress();
            return;
          } catch (err: any) {
            lastError = err;
            if (attempt < maxRetries) {
              activeChunkProgressMap.set(index, 0);
              updateProgress();
              await backoffDelay(attempt);
            }
          }
        }
        throw new Error(`分片 ${index} 失败: ${(lastError as any)?.message}`);
      };

      await new Promise<void>((resolve, reject) => {
        let hasError = false;
        const activeWorkers = new Set<Promise<void>>();

        const spawnWorkers = () => {
          if (hasError || abortController?.signal.aborted) return;

          const targetConcurrency = Math.min(calcConcurrency(currentSpeedMBps), totalChunks);

          while (activeWorkers.size < targetConcurrency && chunkIndices.length > 0) {
            const index = chunkIndices.shift()!;
            const p = processChunk(index)
              .then(() => {
                activeWorkers.delete(p);
                if (chunkIndices.length === 0 && activeWorkers.size === 0) {
                  resolve();
                } else {
                  spawnWorkers();
                }
              })
              .catch((err) => {
                hasError = true;
                abortController?.abort();
                reject(err);
              });
            activeWorkers.add(p);
          }
          if (chunkIndices.length === 0 && activeWorkers.size === 0 && !hasError) {
            resolve();
          }
        };

        spawnWorkers();
      });

      if (abortController.signal.aborted) throw new Error('已取消');

      uploadProgress.value = 99;
      const finishRes = await finishUpload(uploadId, totalChunks);
      const finalKey = (finishRes as any).uploadId;

      uploadProgress.value = 100;
      fileKey.value = finalKey;
      return finalKey;
    } catch (error: any) {
      if (error.message !== '已取消') {
        throw error;
      }
      throw new Error('已取消');
    } finally {
      if (!abortController?.signal.aborted) {
        isUploading.value = false;
      }
    }
  };

  const cancelUpload = () => {
    abortController?.abort();
    isUploading.value = false;
  };

  const removeUploadData = async () => {
    if (fileKey.value) {
      await deleteUpload(fileKey.value).catch(() => {});
      fileKey.value = '';
    }
    uploadedFileName.value = '';
    uploadedFileSize.value = '';
    uploadProgress.value = 0;
  };

  // 组件销毁时自动终止正在进行的上传
  onUnmounted(() => {
    cancelUpload();
  });

  return {
    isUploading,
    uploadProgress,
    uploadedFileName,
    uploadedFileSize,
    fileKey,
    startUpload,
    cancelUpload,
    removeUploadData,
  };
}
