import { ref, onUnmounted } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';
import {
  deleteUpload,
  getUploadStatus,
} from '@/api/files';
import {
  computeFastFileFingerprint,
  deleteUploadResumeRecord,
  getUploadResumeRecords,
  saveUploadResumeRecord,
  type UploadFileHandleLike,
  type UploadResumeRecord,
} from '@/utils/uploadResume';
import { uploadFileInChunks } from '@/utils/chunkedUpload';

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

function getHttpStatus(error: any): number | undefined {
  const responseCode = error?.responseData?.code ?? error?.response?.data?.code;
  return typeof responseCode === 'number'
    ? responseCode
    : error?.status ?? error?.response?.status;
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
    activeUploadId = '';
    activeResumeRecord = null;
    const isCurrentUpload = () => abortController === currentAbortController && !currentAbortController.signal.aborted;

    try {
      const uploadNodeId = options.resumeRecord?.nodeId || targetNodeId;
      const result = await uploadFileInChunks(file, {
        nodeId: uploadNodeId,
        signal: currentAbortController.signal,
        resumeRecord: options.resumeRecord,
        kind: options.kind,
        onSession: (record) => {
          if (!isCurrentUpload()) return;
          activeUploadId = record.uploadId;
          activeResumeRecord = record;
          if (options.fileHandle) {
            record.fileHandle = options.fileHandle;
            record.updatedAt = Date.now();
            void saveUploadResumeRecord(record).catch(() => {});
          }
        },
        onProgress: (progress) => {
          if (!isCurrentUpload()) return;
          uploadProgress.value = Math.max(uploadProgress.value, progress);
        },
        onStatus: (status) => {
          if (!isCurrentUpload()) return;
          if (status === 'merging') uploadProgress.value = 99;
        },
      });
      if (!isCurrentUpload()) throw new Error('已取消');
      const finalKey = result.response.uploadId;
      if (!finalKey) throw new Error('上传合并未返回文件标识');
      activeUploadId = '';
      activeResumeRecord = null;
      fileKey.value = finalKey;
      uploadProgress.value = 100;
      return finalKey;
    } catch (error: any) {
      if (error?.message === '已取消' || currentAbortController.signal.aborted) {
        throw new Error('已取消');
      }
      throw error;
    } finally {
      if (abortController === currentAbortController) {
        abortController = null;
        isUploading.value = false;
      }
    }
  };

  const cancelUpload = async () => {
    abortController?.abort();
    const record = activeResumeRecord;
    if (record && activeResumeRecord === record) {
      record.cancelledAt = Date.now();
      record.updatedAt = Date.now();
      await saveUploadResumeRecord(record).catch(() => {});
    }
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
      if (record.cancelledAt) continue;
      if (kind) {
        const matchesKind = record.kind
          ? record.kind === kind
          : kind === 'package'
            ? /\.(?:zip|mrpack)$/i.test(record.fileName)
            : /\.jar$/i.test(record.fileName);
        if (!matchesKind) continue;
      }

      // 浏览器中的恢复记录可能在 Daemon 重启后仍然存在，但服务端会话和
      // 临时分片已经被清理。只有服务端确认会话仍有效时，才把它显示为可恢复任务。
      try {
        const status = await getUploadStatus(record.uploadId, record.nodeId);
        const receivedChunks = (status.receivedChunks || []).filter(
          (index) => Number.isInteger(index) && index >= 0 && index < record.totalChunks,
        );
        record.receivedChunks = receivedChunks;
        record.updatedAt = Date.now();
        await saveUploadResumeRecord(record).catch(() => {});
      } catch (error) {
        const status = getHttpStatus(error);
        if (status === 403 || status === 404) {
          // 会话已过期、失效或属于已重启前的 Daemon，不能再提示用户恢复。
          await deleteUploadResumeRecord(record.key).catch(() => {});
        }
        // 网络暂不可用时保留本地记录，但不把未经服务端确认的记录显示为未完成任务。
        continue;
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
  onUnmounted(() => {
    void cancelUpload();
  });

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
