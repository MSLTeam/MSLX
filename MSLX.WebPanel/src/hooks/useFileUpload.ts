import { ref, onUnmounted } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';
import {
  deleteUpload,
  finishUpload,
  getUploadStatus,
  initUpload,
  rememberUploadTarget,
  uploadChunk,
} from '@/api/files';
import {
  computeFastFileFingerprint,
  computeFileSha256WithOptions,
  computeChunkCrc32c,
  deleteUploadResumeRecord,
  getUploadResumeRecords,
  saveUploadResumeRecord,
  type UploadFileHandleLike,
  type UploadResumeRecord,
} from '@/utils/uploadResume';

const LARGE_FILE_SIZE = 200 * 1024 * 1024;
const LARGE_CHUNK_SIZE = 30 * 1024 * 1024;
const SMALL_CHUNK_SIZE = 10 * 1024 * 1024;
const MAX_RETRIES = 5;
const CHUNK_WATCHDOG_MS = 60 * 1000;

export interface UploadStartOptions {
  fileHandle?: UploadFileHandleLike;
  resumeRecord?: UploadResumeRecord;
  kind?: 'package' | 'core';
}

export interface PendingUploadResume {
  record: UploadResumeRecord;
  file?: File;
  fileHandle?: UploadFileHandleLike;
  permissionRequired: boolean;
}

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
  return status === 408 || status === 422 || status === 425 || status === 429 || status >= 500;
}

export function useFileUpload() {
  const targetNodeId = getPageNodeId();
  const isUploading = ref(false);
  const uploadProgress = ref(0);
  const uploadedFileName = ref('');
  const uploadedFileSize = ref('');
  const fileKey = ref(''); // 成功后的文件 Key

  let abortController: AbortController | null = null;
  let activeUploadId = '';
  let activeResumeRecord: UploadResumeRecord | null = null;

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const startUpload = async (file: File, options: UploadStartOptions = {}): Promise<string> => {
    abortController?.abort();
    const currentAbortController = new AbortController();
    abortController = currentAbortController;

    isUploading.value = true;
    uploadProgress.value = 0;
    uploadedFileName.value = file.name;
    uploadedFileSize.value = formatFileSize(file.size);
    fileKey.value = '';

    const failBeforeUpload = (error: unknown): never => {
      if (abortController === currentAbortController) {
        abortController = null;
        isUploading.value = false;
      }
      if (currentAbortController.signal.aborted || (error as any)?.message === '已取消') {
        throw new Error('已取消');
      }
      throw error;
    };

    const chunkSize = file.size > LARGE_FILE_SIZE ? LARGE_CHUNK_SIZE : SMALL_CHUNK_SIZE;
    const totalChunks = Math.ceil(file.size / chunkSize);
    let fingerprint: string;
    try {
      fingerprint = await computeFastFileFingerprint(file);
    } catch (error) {
      return failBeforeUpload(error);
    }
    const uploadNodeId = options.resumeRecord?.nodeId || targetNodeId;
    const receivedChunks = new Set<number>();
    const activeChunkProgressMap = new Map<number, number>();
    const chunkIndices = Array.from({ length: totalChunks }, (_, index) => index);
    let committedBytes = 0;
    let currentSpeedMBps = 0;
    let lastUpdateTime = 0;
    let statusRecoveryRequested = false;
    const startTime = Date.now();
    let resumeRecord = options.resumeRecord;
    let fileSha256 = resumeRecord?.sha256;
    let fileSha256Promise: Promise<string> | null = null;

    if (!resumeRecord) {
      const existingRecords = await getUploadResumeRecords(targetNodeId).catch(() => []);
      resumeRecord = existingRecords.find(
        (record) =>
          record.fileName === file.name &&
          record.fileSize === file.size &&
          record.lastModified === file.lastModified &&
          record.fingerprint === fingerprint &&
          record.chunkSize === chunkSize &&
          record.totalChunks === totalChunks,
      );
    }

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

    const persistResumeRecord = async () => {
      if (!activeResumeRecord) return;
      activeResumeRecord.receivedChunks = Array.from(receivedChunks).sort((a, b) => a - b);
      activeResumeRecord.updatedAt = Date.now();
      try {
        await saveUploadResumeRecord(activeResumeRecord);
      } catch {
        // 某些浏览器无法结构化克隆 FileSystemFileHandle，保留元数据供重新选择文件恢复。
        if (activeResumeRecord.fileHandle) {
          activeResumeRecord.fileHandle = undefined;
          await saveUploadResumeRecord(activeResumeRecord).catch(() => {});
        }
      }
    };

    const rememberComputedSha256 = async (sha256: string) => {
      fileSha256 = sha256;
      if (activeResumeRecord && !activeResumeRecord.sha256) {
        activeResumeRecord.sha256 = sha256;
        await persistResumeRecord();
      }
    };

    if (!fileSha256) {
      fileSha256Promise = computeFileSha256WithOptions(file, {
        signal: currentAbortController.signal,
        // 哈希在 Worker 中后台计算，不参与上传进度显示。
      });
      // 上传可能先失败或被取消，提前挂载 rejection handler，避免后台哈希产生未处理异常。
      fileSha256Promise.catch(() => {});
    }

    if (currentAbortController.signal.aborted) {
      return failBeforeUpload(new Error('已取消'));
    }

    const syncStatus = async (uploadId: string) => {
      const status = await getUploadStatus(uploadId, uploadNodeId);
      for (const index of status.receivedChunks || []) {
        if (index >= 0 && index < totalChunks) markReceived(index);
      }
      await persistResumeRecord();
      statusRecoveryRequested = false;
      return status;
    };

    const onlineHandler = () => {
      statusRecoveryRequested = true;
    };
    window.addEventListener('online', onlineHandler);

    try {
      if (currentAbortController.signal.aborted) throw new Error('已取消');
      let uploadId = resumeRecord?.uploadId || '';

      if (uploadId) {
        rememberUploadTarget(uploadId, uploadNodeId);
        activeResumeRecord = resumeRecord || null;
        if (activeResumeRecord && options.kind && !activeResumeRecord.kind) {
          activeResumeRecord.kind = options.kind;
        }
        if (fileSha256Promise) void fileSha256Promise.then(rememberComputedSha256).catch(() => {});
        try {
          await syncStatus(uploadId);
        } catch (error: any) {
          const status = getHttpStatus(error);
          if (status === 403 || status === 404) {
            await deleteUploadResumeRecord(resumeRecord!.key).catch(() => {});
            resumeRecord = undefined;
            activeResumeRecord = null;
            uploadId = '';
          } else {
            throw error;
          }
        }
      }

      if (!uploadId) {
        const initRes = await initUpload(uploadNodeId, {
          fileName: file.name,
          fileSize: file.size,
          lastModified: file.lastModified,
          chunkSize,
          totalChunks,
          fingerprint,
        });
        uploadId = (initRes as any).uploadId;
        rememberUploadTarget(uploadId, uploadNodeId);
        activeResumeRecord = {
          key: `${uploadNodeId}:${fingerprint}`,
          uploadId,
          nodeId: uploadNodeId,
          fileName: file.name,
          fileSize: file.size,
          lastModified: file.lastModified,
          chunkSize,
          totalChunks,
          fingerprint,
          kind: options.kind,
          sha256: fileSha256,
          receivedChunks: [],
          updatedAt: Date.now(),
          fileHandle: options.fileHandle,
        };
        if (fileSha256Promise) void fileSha256Promise.then(rememberComputedSha256).catch(() => {});
        for (const index of (initRes as any).receivedChunks || []) {
          if (index >= 0 && index < totalChunks) markReceived(index);
        }
        await persistResumeRecord();
      } else if (options.fileHandle && activeResumeRecord) {
        activeResumeRecord.fileHandle = options.fileHandle;
        await persistResumeRecord();
      }

      activeUploadId = uploadId;
      if (!uploadId) throw new Error('无法获取上传凭证');

      const processChunk = async (index: number) => {
        if (currentAbortController.signal.aborted) throw new Error('已取消');
        const chunkBlob = file.slice(index * chunkSize, Math.min(file.size, (index + 1) * chunkSize));
        const chunkCrc32c = await computeChunkCrc32c(chunkBlob, { signal: currentAbortController.signal });
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
              uploadNodeId,
              chunkCrc32c,
            );
            markReceived(index);
            await persistResumeRecord();
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

      const runChunkQueue = async (pendingIndices: number[]) => {
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

            while (pendingIndices.length > 0 && receivedChunks.has(pendingIndices[0])) {
              pendingIndices.shift();
            }

            const targetConcurrency = Math.min(calcConcurrency(currentSpeedMBps), Math.max(1, pendingIndices.length));
            while (activeWorkers.size < targetConcurrency && pendingIndices.length > 0) {
              const index = pendingIndices.shift()!;

              const worker = processChunk(index)
                .then(() => {
                  activeWorkers.delete(worker);
                  if (pendingIndices.length === 0 && activeWorkers.size === 0) resolve();
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

            if (pendingIndices.length === 0 && activeWorkers.size === 0 && !hasError) resolve();
          };

          spawnWorkers();
        });
      };

      await runChunkQueue(chunkIndices);

      if (currentAbortController.signal.aborted) throw new Error('已取消');

      if (!fileSha256) {
        if (!fileSha256Promise) throw new Error('无法计算文件完整性校验值');
        await rememberComputedSha256(await fileSha256Promise);
      }

      uploadProgress.value = 99;
      let finishRes: any;
      for (let attempt = 1; attempt <= 3; attempt++) {
        try {
          finishRes = await finishUpload(uploadId, totalChunks, uploadNodeId, file.size, chunkSize, fileSha256);
          break;
        } catch (error: any) {
          if (getHttpStatus(error) === 422) {
            const invalidChunkValues = (error?.responseData?.data?.invalidChunks ||
              error?.responseData?.Data?.invalidChunks || []) as unknown[];
            const invalidChunks = invalidChunkValues
              .map((index) => Number(index))
              .filter((index): index is number => Number.isInteger(index) && index >= 0 && index < totalChunks);
            if (invalidChunks.length > 0) {
              const retryQueue: number[] = [];
              for (const index of [...new Set(invalidChunks)]) {
                if (receivedChunks.delete(index)) {
                  committedBytes = Math.max(0, committedBytes - chunkBytes(index));
                }
                activeChunkProgressMap.delete(index);
                retryQueue.push(index);
              }
              // 完整性失败的分片进入独立队列，当前上传调度完成后再重传，避免插入正在运行的任务。
              await runChunkQueue(retryQueue);
              continue;
            }
          }
          if (!isRetryableUploadError(error) || attempt >= 3) throw error;
          await backoffDelay(attempt);
          await syncStatus(uploadId).catch(() => {});
        }
      }

      const finalKey = finishRes?.uploadId;
      if (!finalKey) throw new Error('上传合并未返回文件标识');
      uploadProgress.value = 100;
      fileKey.value = finalKey;
      if (activeResumeRecord) {
        await deleteUploadResumeRecord(activeResumeRecord.key).catch(() => {});
      }
      activeResumeRecord = null;
      activeUploadId = '';
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
    const uploadId = activeUploadId;
    const finalKey = fileKey.value;
    if (finalKey) {
      await deleteUpload(finalKey, targetNodeId).catch(() => {});
      fileKey.value = '';
    }
    if (uploadId && uploadId !== finalKey) {
      await deleteUpload(uploadId, activeResumeRecord?.nodeId || targetNodeId).catch(() => {});
    }
    if (activeResumeRecord) {
      await deleteUploadResumeRecord(activeResumeRecord.key).catch(() => {});
    }
    activeUploadId = '';
    activeResumeRecord = null;
    uploadedFileName.value = '';
    uploadedFileSize.value = '';
    uploadProgress.value = 0;
  };

  const restorePendingUpload = async (kind?: 'package' | 'core'): Promise<PendingUploadResume | null> => {
    const records = await getUploadResumeRecords(targetNodeId).catch(() => []);
    for (const record of records) {
      if (!record.uploadId || !record.fingerprint) continue;
      if (kind) {
        const matchesKind = record.kind
          ? record.kind === kind
          : kind === 'package'
            ? /\.(?:zip|mrpack)$/i.test(record.fileName)
            : /\.jar$/i.test(record.fileName);
        if (!matchesKind) continue;
      }

      const fileHandle = record.fileHandle;
      if (!fileHandle) {
        return { record, permissionRequired: true };
      }

      try {
        let permission = await fileHandle.queryPermission?.({ mode: 'read' });
        if (permission === 'granted') {
          const file = await fileHandle.getFile();
          const fingerprint = await computeFastFileFingerprint(file);
          if (fingerprint === record.fingerprint) {
            return { record, file, fileHandle, permissionRequired: false };
          }
          await deleteUploadResumeRecord(record.key).catch(() => {});
          continue;
        }

        if (permission === 'prompt' && fileHandle.requestPermission) {
          // 刷新后自动请求权限可能被浏览器拦截，交给页面按钮在用户操作时重试。
          permission = 'prompt';
        }
        return { record, fileHandle, permissionRequired: permission !== 'granted' };
      } catch {
        return { record, fileHandle, permissionRequired: true };
      }
    }
    return null;
  };

  const resumeUpload = (file: File, record: UploadResumeRecord, fileHandle?: UploadFileHandleLike) => {
    return startUpload(file, { resumeRecord: record, fileHandle });
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
    restorePendingUpload,
    resumeUpload,
    cancelUpload,
    removeUploadData,
  };
}
