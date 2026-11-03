import {
  finishUpload,
  getUploadStatus,
  initUpload,
  rememberUploadTarget,
  uploadChunk,
} from '@/api/files';
import type { UploadFinishResponse } from '@/api/model/files';
import {
  computeChunkCrc32c,
  computeFastFileFingerprint,
  computeFileBlake3WithOptions,
  deleteUploadResumeRecord,
  getUploadResumeRecords,
  saveUploadResumeRecord,
  type UploadResumeRecord,
} from '@/utils/uploadResume';

const LARGE_FILE_SIZE = 200 * 1024 * 1024;
const LARGE_CHUNK_SIZE = 30 * 1024 * 1024;
const SMALL_CHUNK_SIZE = 10 * 1024 * 1024;
const MAX_RETRIES = 5;
const CHUNK_WATCHDOG_MS = 60 * 1000;

export type UploadResumeKind = 'package' | 'core' | 'file';

export interface ChunkedUploadOptions {
  nodeId: string;
  signal: AbortSignal;
  resumeRecord?: UploadResumeRecord;
  kind?: UploadResumeKind;
  progressMax?: number;
  onProgress?: (_progress: number, _speedMBps: number) => void;
  onStatus?: (_status: 'uploading' | 'merging') => void;
  onSession?: (_record: UploadResumeRecord) => void | Promise<void>;
}

export interface ChunkedUploadResult {
  uploadId: string;
  blake3: string;
  response: UploadFinishResponse;
  resumeRecord: UploadResumeRecord;
}

function getHttpStatus(error: any): number | undefined {
  const responseCode = error?.responseData?.code ?? error?.response?.data?.code;
  return typeof responseCode === 'number' ? responseCode : error?.status ?? error?.response?.status;
}

function isRetryableUploadError(error: any): boolean {
  const status = getHttpStatus(error);
  if (status === undefined) return true;
  return status === 408 || status === 422 || status === 425 || status === 429 || status >= 500;
}

function backoffDelay(attempt: number): Promise<void> {
  const base = Math.min(30_000, 500 * 2 ** attempt);
  const jitter = Math.random() * base * 0.3;
  return new Promise((resolve) => setTimeout(resolve, base + jitter));
}

function calcConcurrency(speedMBps: number): number {
  if (speedMBps <= 0) return 2;
  if (speedMBps < 0.5) return 1;
  if (speedMBps < 2) return 2;
  return 4;
}

function chunkSizeFor(file: File): number {
  return file.size > LARGE_FILE_SIZE ? LARGE_CHUNK_SIZE : SMALL_CHUNK_SIZE;
}

function chunkBytes(file: File, chunkSize: number, index: number): number {
  const start = index * chunkSize;
  return Math.max(0, Math.min(file.size, start + chunkSize) - start);
}

async function findResumeRecord(
  file: File,
  nodeId: string,
  fingerprint: string,
  chunkSize: number,
  totalChunks: number,
  kind?: UploadResumeKind,
): Promise<UploadResumeRecord | undefined> {
  const records = await getUploadResumeRecords(nodeId).catch(() => []);
  return records.find(
    (record) =>
      (!kind || !record.kind || record.kind === kind) &&
      record.fileName === file.name &&
      record.fileSize === file.size &&
      record.lastModified === file.lastModified &&
      record.fingerprint === fingerprint &&
      record.chunkSize === chunkSize &&
      record.totalChunks === totalChunks,
  );
}

function getInvalidChunks(error: any, totalChunks: number): number[] {
  const data = error?.responseData?.data ?? error?.responseData?.Data ?? error?.response?.data?.data;
  return [
    ...(Array.isArray(data?.invalidChunks) ? data.invalidChunks : []),
    ...(Array.isArray(data?.missingChunks) ? data.missingChunks : []),
  ]
    .map((index) => Number(index))
    .filter((index): index is number => Number.isInteger(index) && index >= 0 && index < totalChunks);
}

export async function uploadFileInChunks(
  file: File,
  options: ChunkedUploadOptions,
): Promise<ChunkedUploadResult> {
  const { nodeId, signal } = options;
  const chunkSize = chunkSizeFor(file);
  const totalChunks = Math.ceil(file.size / chunkSize);
  const progressMax = options.progressMax ?? 98;
  const fingerprint = await computeFastFileFingerprint(file);
  let resumeRecord = options.resumeRecord || await findResumeRecord(
    file,
    nodeId,
    fingerprint,
    chunkSize,
    totalChunks,
    options.kind,
  );
  let uploadId = resumeRecord?.uploadId || '';
  const receivedChunks = new Set<number>();

  const ensureNotAborted = () => {
    if (signal.aborted) throw new Error('已取消');
  };

  try {
    ensureNotAborted();
    if (uploadId) {
      rememberUploadTarget(uploadId, nodeId);
      try {
        const status = await getUploadStatus(uploadId, nodeId);
        for (const index of status.receivedChunks || []) {
          if (index >= 0 && index < totalChunks) receivedChunks.add(index);
        }
      } catch (error) {
        const status = getHttpStatus(error);
        if (status === 403 || status === 404) {
          await deleteUploadResumeRecord(resumeRecord!.key).catch(() => {});
          resumeRecord = undefined;
          uploadId = '';
        } else {
          throw error;
        }
      }
    }

    if (!uploadId) {
      const initRes = await initUpload(nodeId, {
        fileName: file.name,
        fileSize: file.size,
        lastModified: file.lastModified,
        chunkSize,
        totalChunks,
        fingerprint,
      });
      uploadId = initRes.uploadId;
      for (const index of initRes.receivedChunks || []) {
        if (index >= 0 && index < totalChunks) receivedChunks.add(index);
      }
      resumeRecord = {
        key: `${nodeId}:${fingerprint}`,
        uploadId,
        nodeId,
        fileName: file.name,
        fileSize: file.size,
        lastModified: file.lastModified,
        chunkSize,
        totalChunks,
        fingerprint,
        kind: options.kind,
        receivedChunks: [],
        updatedAt: Date.now(),
      };
    }

    if (!resumeRecord) throw new Error('无法建立上传恢复记录');
    resumeRecord.cancelledAt = undefined;
    resumeRecord.receivedChunks = [...receivedChunks].sort((a, b) => a - b);
    resumeRecord.updatedAt = Date.now();
    await saveUploadResumeRecord(resumeRecord).catch(() => {});
    await options.onSession?.(resumeRecord);

    let committedBytes = 0;
    for (const index of receivedChunks) committedBytes += chunkBytes(file, chunkSize, index);
    let currentSpeedMBps = 0;
    let lastUpdate = 0;
    const startTime = Date.now();
    const activeChunkProgressMap = new Map<number, number>();
    const updateProgress = () => {
      const now = Date.now();
      if (now - lastUpdate < 100) return;
      lastUpdate = now;
      let activeLoadedBytes = 0;
      for (const loaded of activeChunkProgressMap.values()) activeLoadedBytes += loaded;
      const totalLoadedBytes = committedBytes + activeLoadedBytes;
      const progress = file.size === 0 ? progressMax : Math.min((totalLoadedBytes / file.size) * progressMax, progressMax);
      const elapsed = (now - startTime) / 1000;
      if (elapsed > 0) currentSpeedMBps = totalLoadedBytes / 1024 / 1024 / elapsed;
      options.onProgress?.(Number(progress.toFixed(1)), currentSpeedMBps);
    };

    updateProgress();
    const fileBlake3Promise = resumeRecord.blake3
      ? Promise.resolve(resumeRecord.blake3)
      : computeFileBlake3WithOptions(file, { signal });
    fileBlake3Promise.catch(() => {});

    const processChunk = async (index: number) => {
      ensureNotAborted();
      const chunk = file.slice(index * chunkSize, Math.min(file.size, (index + 1) * chunkSize));
      const chunkCrc32c = await computeChunkCrc32c(chunk, { signal });
      let lastError: any;

      for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
        ensureNotAborted();
        const attemptController = new AbortController();
        const abortAttempt = () => attemptController.abort();
        signal.addEventListener('abort', abortAttempt, { once: true });
        let watchdogTimer = window.setTimeout(() => attemptController.abort(), CHUNK_WATCHDOG_MS);
        const touchWatchdog = () => {
          window.clearTimeout(watchdogTimer);
          watchdogTimer = window.setTimeout(() => attemptController.abort(), CHUNK_WATCHDOG_MS);
        };

        try {
          await uploadChunk(
            uploadId,
            index,
            chunk,
            (event: any) => {
              touchWatchdog();
              if (event?.loaded) {
                activeChunkProgressMap.set(index, event.loaded);
                updateProgress();
              }
            },
            attemptController.signal,
            nodeId,
            chunkCrc32c,
          );
          receivedChunks.add(index);
          committedBytes += chunk.size;
          activeChunkProgressMap.delete(index);
          resumeRecord!.receivedChunks = [...receivedChunks].sort((a, b) => a - b);
          resumeRecord!.updatedAt = Date.now();
          await saveUploadResumeRecord(resumeRecord!).catch(() => {});
          updateProgress();
          return;
        } catch (error: any) {
          lastError = error;
          if (signal.aborted) throw new Error('已取消');
          if (attempt < MAX_RETRIES && isRetryableUploadError(error)) {
            activeChunkProgressMap.set(index, 0);
            updateProgress();
            await backoffDelay(attempt);
          }
        } finally {
          window.clearTimeout(watchdogTimer);
          signal.removeEventListener('abort', abortAttempt);
        }
      }

      try {
        const status = await getUploadStatus(uploadId, nodeId);
        if (status.receivedChunks?.includes(index)) {
          receivedChunks.add(index);
          return;
        }
      } catch {
        // 保留原始错误，避免状态查询失败掩盖分片错误。
      }
      throw lastError || new Error(`分片 ${index} 失败`);
    };

    const runChunkQueue = async (pendingIndices: number[]) => {
      await new Promise<void>((resolve, reject) => {
        let hasError = false;
        const activeWorkers = new Set<Promise<void>>();
        const spawnWorkers = () => {
          if (hasError || signal.aborted) return;
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
                if (hasError) return;
                hasError = true;
                reject(error);
              });
            activeWorkers.add(worker);
          }
          if (pendingIndices.length === 0 && activeWorkers.size === 0 && !hasError) resolve();
        };
        spawnWorkers();
      });
    };

    await runChunkQueue(Array.from({ length: totalChunks }, (_, index) => index).filter((index) => !receivedChunks.has(index)));
    ensureNotAborted();
    options.onStatus?.('merging');
    const blake3 = await fileBlake3Promise;
    if (!resumeRecord.blake3) {
      resumeRecord.blake3 = blake3;
      resumeRecord.updatedAt = Date.now();
      await saveUploadResumeRecord(resumeRecord).catch(() => {});
    }
    let finishResponse: UploadFinishResponse | undefined;

    for (let attempt = 1; attempt <= 3; attempt++) {
      try {
        finishResponse = await finishUpload(uploadId, totalChunks, nodeId, file.size, chunkSize, blake3);
        break;
      } catch (error: any) {
        if (attempt >= 3) throw error;
        const invalidChunks = [...new Set(getInvalidChunks(error, totalChunks))];
        if (invalidChunks.length > 0) {
          for (const index of invalidChunks) {
            if (receivedChunks.delete(index)) committedBytes = Math.max(0, committedBytes - chunkBytes(file, chunkSize, index));
            activeChunkProgressMap.delete(index);
          }
          await runChunkQueue(invalidChunks);
          continue;
        }
        if (!isRetryableUploadError(error)) throw error;
        await backoffDelay(attempt);
      }
    }

    if (!finishResponse) throw new Error('上传合并未返回结果');
    await deleteUploadResumeRecord(resumeRecord.key).catch(() => {});
    return { uploadId, blake3, response: finishResponse, resumeRecord };
  } catch (error: any) {
    if (error?.message === '已取消' || signal.aborted) {
      if (resumeRecord) {
        resumeRecord.cancelledAt = Date.now();
        resumeRecord.updatedAt = Date.now();
        await saveUploadResumeRecord(resumeRecord).catch(() => {});
      }
      throw new Error('已取消');
    }
    throw error;
  }
}

