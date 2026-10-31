import { ref, onUnmounted } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';
import {
  deleteUpload,
  finishUpload,
  getUploadStatus,
  initUpload,
  uploadChunk,
} from '@/api/files';

const LARGE_FILE_SIZE = 200 * 1024 * 1024;
const LARGE_CHUNK_SIZE = 30 * 1024 * 1024;
const SMALL_CHUNK_SIZE = 10 * 1024 * 1024;
const MAX_RETRIES = 5;
const CHUNK_WATCHDOG_MS = 60 * 1000;

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
  return new Promise((resolve) => setTimeout(resolve, base + jitter));
}

function getHttpStatus(error: any): number | undefined {
  return error?.status ?? error?.response?.status;
}

function isRetryableUploadError(error: any): boolean {
  const status = getHttpStatus(error);
  if (status === undefined) return true;
  return status === 408 || status === 425 || status === 429 || status >= 500;
}

export function useFileUpload() {
  const targetNodeId = getPageNodeId();
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
    abortController?.abort();
    const currentAbortController = new AbortController();
    abortController = currentAbortController;

    isUploading.value = true;
    uploadProgress.value = 0;
    uploadedFileName.value = file.name;
    uploadedFileSize.value = formatFileSize(file.size);
    fileKey.value = '';

    const chunkSize = file.size > LARGE_FILE_SIZE ? LARGE_CHUNK_SIZE : SMALL_CHUNK_SIZE;
    const totalChunks = Math.ceil(file.size / chunkSize);
    const receivedChunks = new Set<number>();
    const activeChunkProgressMap = new Map<number, number>();
    const chunkIndices = Array.from({ length: totalChunks }, (_, index) => index);
    let committedBytes = 0;
    let currentSpeedMBps = 0;
    let lastUpdateTime = 0;
    let statusRecoveryRequested = false;
    const startTime = Date.now();

    const chunkBytes = (index: number) => {
      const start = index * chunkSize;
      return Math.max(0, Math.min(file.size, start + chunkSize) - start);
    };

    const updateProgress = () => {
      const now = Date.now();
      if (now - lastUpdateTime < 100) return;
      lastUpdateTime = now;

      let activeLoadedBytes = 0;
      for (const loaded of activeChunkProgressMap.values()) activeLoadedBytes += loaded;
      const totalLoadedBytes = committedBytes + activeLoadedBytes;
      const percent = file.size === 0 ? 98 : Math.min((totalLoadedBytes / file.size) * 98, 98);
      uploadProgress.value = Math.max(uploadProgress.value, Number(percent.toFixed(1)));

      const elapsed = (now - startTime) / 1000;
      if (elapsed > 0) currentSpeedMBps = totalLoadedBytes / 1024 / 1024 / elapsed;
    };

    const markReceived = (index: number) => {
      if (receivedChunks.has(index)) return;
      receivedChunks.add(index);
      committedBytes += chunkBytes(index);
      activeChunkProgressMap.delete(index);
      updateProgress();
    };

    const syncStatus = async (uploadId: string) => {
      const status = await getUploadStatus(uploadId);
      for (const index of status.receivedChunks || []) {
        if (index >= 0 && index < totalChunks) markReceived(index);
      }
      statusRecoveryRequested = false;
      return status;
    };

    const onlineHandler = () => {
      statusRecoveryRequested = true;
    };
    window.addEventListener('online', onlineHandler);

    try {
      const initRes = await initUpload(targetNodeId);
      const uploadId = (initRes as any).uploadId;
      if (!uploadId) throw new Error('无法获取上传凭证');

      const processChunk = async (index: number) => {
        if (currentAbortController.signal.aborted) throw new Error('已取消');
        const chunkBlob = file.slice(index * chunkSize, Math.min(file.size, (index + 1) * chunkSize));
        let lastError: any;

        for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
          if (currentAbortController.signal.aborted) throw new Error('已取消');

          const attemptController = new AbortController();
          const abortAttempt = () => attemptController.abort();
          currentAbortController.signal.addEventListener('abort', abortAttempt, { once: true });
          let watchdogTimer = window.setTimeout(() => attemptController.abort(), CHUNK_WATCHDOG_MS);
          const touchWatchdog = () => {
            window.clearTimeout(watchdogTimer);
            watchdogTimer = window.setTimeout(() => attemptController.abort(), CHUNK_WATCHDOG_MS);
          };

          try {
            await uploadChunk(
              uploadId,
              index,
              chunkBlob,
              (event: any) => {
                touchWatchdog();
                if (event?.loaded) {
                  activeChunkProgressMap.set(index, event.loaded);
                  updateProgress();
                }
              },
              attemptController.signal,
            );
            markReceived(index);
            return;
          } catch (error: any) {
            lastError = error;
            if (currentAbortController.signal.aborted) throw new Error('已取消');
            if (!isRetryableUploadError(error) || attempt >= MAX_RETRIES) break;
            activeChunkProgressMap.set(index, 0);
            updateProgress();
            await backoffDelay(attempt);
          } finally {
            window.clearTimeout(watchdogTimer);
            currentAbortController.signal.removeEventListener('abort', abortAttempt);
          }
        }

        try {
          const status = await syncStatus(uploadId);
          if (status.receivedChunks?.includes(index)) return;
        } catch {
          // 保留原始错误，避免状态查询失败掩盖真正的网络错误。
        }

        const status = getHttpStatus(lastError);
        const suffix = status ? ` (HTTP ${status})` : '';
        throw new Error(`分片 ${index} 失败${suffix}: ${lastError?.message || '网络连接中断'}`);
      };

      await new Promise<void>((resolve, reject) => {
        let hasError = false;
        const activeWorkers = new Set<Promise<void>>();

        const spawnWorkers = () => {
          if (hasError || currentAbortController.signal.aborted) return;

          if (statusRecoveryRequested) {
            void syncStatus(uploadId).catch(() => {
              statusRecoveryRequested = true;
            });
          }

          const targetConcurrency = Math.min(calcConcurrency(currentSpeedMBps), Math.max(1, chunkIndices.length));
          while (activeWorkers.size < targetConcurrency && chunkIndices.length > 0) {
            const index = chunkIndices.shift()!;
            if (receivedChunks.has(index)) {
              spawnWorkers();
              continue;
            }

            let worker: Promise<void>;
            worker = processChunk(index)
              .then(() => {
                activeWorkers.delete(worker);
                if (chunkIndices.length === 0 && activeWorkers.size === 0) resolve();
                else spawnWorkers();
              })
              .catch((error) => {
                activeWorkers.delete(worker);
                hasError = true;
                currentAbortController.abort();
                reject(error);
              });
            activeWorkers.add(worker);
          }

          if (chunkIndices.length === 0 && activeWorkers.size === 0 && !hasError) resolve();
        };

        spawnWorkers();
      });

      if (currentAbortController.signal.aborted) throw new Error('已取消');

      uploadProgress.value = 99;
      let finishRes: any;
      for (let attempt = 1; attempt <= 3; attempt++) {
        try {
          finishRes = await finishUpload(uploadId, totalChunks);
          break;
        } catch (error: any) {
          if (!isRetryableUploadError(error) || attempt >= 3) throw error;
          await backoffDelay(attempt);
          await syncStatus(uploadId).catch(() => {});
        }
      }

      const finalKey = finishRes?.uploadId;
      if (!finalKey) throw new Error('上传合并未返回文件标识');
      uploadProgress.value = 100;
      fileKey.value = finalKey;
      return finalKey;
    } catch (error: any) {
      if (error?.message === '已取消' || currentAbortController.signal.aborted) throw new Error('已取消');
      throw error;
    } finally {
      window.removeEventListener('online', onlineHandler);
      if (abortController === currentAbortController) {
        abortController = null;
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
  onUnmounted(() => cancelUpload());

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
