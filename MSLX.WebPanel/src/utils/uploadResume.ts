import { blake3 } from '@noble/hashes/blake3.js';

export interface UploadFileHandleLike {
  getFile(): Promise<File>;
  queryPermission?: (descriptor?: { mode?: 'read' | 'readwrite' }) => Promise<PermissionState>;
  requestPermission?: (descriptor?: { mode?: 'read' | 'readwrite' }) => Promise<PermissionState>;
}

export interface UploadResumeRecord {
  key: string;
  uploadId: string;
  nodeId: string;
  fileName: string;
  fileSize: number;
  lastModified: number;
  chunkSize: number;
  totalChunks: number;
  fingerprint: string;
  kind?: 'package' | 'core';
  blake3?: string;
  receivedChunks: number[];
  updatedAt: number;
  fileHandle?: UploadFileHandleLike;
}

const DB_NAME = 'mslx-upload-resume';
const STORE_NAME = 'sessions';
const DB_VERSION = 1;
const HASH_WINDOW_SIZE = 1024 * 1024;
const UPLOAD_RESUME_TTL_MS = 2 * 60 * 60 * 1000;

// CRC32C (Castagnoli), reflected polynomial. The table is shared by all
// chunks so calculating a checksum does not allocate per byte.
const CRC32C_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let value = i;
    for (let bit = 0; bit < 8; bit++) {
      value = (value & 1) ? (0x82f63b78 ^ (value >>> 1)) : (value >>> 1);
    }
    table[i] = value >>> 0;
  }
  return table;
})();

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (!('indexedDB' in window)) {
      reject(new Error('当前浏览器不支持 IndexedDB'));
      return;
    }

    const request = window.indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(STORE_NAME)) {
        const store = database.createObjectStore(STORE_NAME, { keyPath: 'key' });
        store.createIndex('nodeId', 'nodeId', { unique: false });
        store.createIndex('updatedAt', 'updatedAt', { unique: false });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error || new Error('打开上传恢复存储失败'));
  });
}

async function withStore<T>(
  mode: IDBTransactionMode,
  action: (store: IDBObjectStore, resolve: (value: T) => void, reject: (reason?: unknown) => void) => void,
): Promise<T> {
  const database = await openDatabase();
  return new Promise<T>((resolve, reject) => {
    const transaction = database.transaction(STORE_NAME, mode);
    const store = transaction.objectStore(STORE_NAME);
    action(store, resolve, reject);
    transaction.oncomplete = () => database.close();
    transaction.onerror = () => reject(transaction.error || new Error('访问上传恢复存储失败'));
    transaction.onabort = () => reject(transaction.error || new Error('上传恢复存储事务已取消'));
  });
}

export async function saveUploadResumeRecord(record: UploadResumeRecord): Promise<void> {
  await withStore<void>('readwrite', (store, resolve, reject) => {
    const request = store.put(record);
    request.onsuccess = () => resolve();
    request.onerror = () => reject(request.error || new Error('保存上传恢复记录失败'));
  });
}

export async function getUploadResumeRecords(nodeId: string): Promise<UploadResumeRecord[]> {
  return withStore<UploadResumeRecord[]>('readwrite', (store, resolve, reject) => {
    const request = store.getAll();
    request.onsuccess = () => {
      const cutoff = Date.now() - UPLOAD_RESUME_TTL_MS;
      const allRecords = request.result as UploadResumeRecord[];
      const records = allRecords
        .filter((record) => record.updatedAt >= cutoff)
        .filter((record) => record.nodeId === nodeId)
        .sort((a, b) => b.updatedAt - a.updatedAt);
      for (const record of allRecords) {
        if (record.updatedAt < cutoff) store.delete(record.key);
      }
      resolve(records);
    };
    request.onerror = () => reject(request.error || new Error('读取上传恢复记录失败'));
  });
}

export async function deleteUploadResumeRecord(key: string): Promise<void> {
  await withStore<void>('readwrite', (store, resolve, reject) => {
    const request = store.delete(key);
    request.onsuccess = () => resolve();
    request.onerror = () => reject(request.error || new Error('删除上传恢复记录失败'));
  });
}

function toHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

export async function computeFastFileFingerprint(file: File): Promise<string> {
  const windowSize = Math.min(HASH_WINDOW_SIZE, file.size);
  const head = new Uint8Array(await file.slice(0, windowSize).arrayBuffer());
  const tailStart = Math.max(0, file.size - windowSize);
  const tail = new Uint8Array(await file.slice(tailStart, file.size).arrayBuffer());

  const metadata = new TextEncoder().encode(
    [file.name, file.size, file.lastModified, tailStart].join('|'),
  );
  const input = new Uint8Array(metadata.length + head.length + tail.length);
  input.set(metadata, 0);
  input.set(head, metadata.length);
  input.set(tail, metadata.length + head.length);

  return `head-tail-blake3:${file.size}:${toHex(blake3(input))}`;
}

/**
 * 计算单个上传分片的 CRC32C。只读取当前分片，适合在上传前做快速完整性校验。
 */
async function computeChunkCrc32cLocal(chunk: Blob | ArrayBuffer): Promise<string> {
  const bytes = new Uint8Array(chunk instanceof Blob ? await chunk.arrayBuffer() : chunk);
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc = CRC32C_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }
  return ((crc ^ 0xffffffff) >>> 0).toString(16).padStart(8, '0');
}

interface ChunkCrc32cOptions {
  useWorker?: boolean;
  signal?: AbortSignal;
}

function computeChunkCrc32cWithWorker(chunk: Blob, signal?: AbortSignal): Promise<string> {
  return new Promise((resolve, reject) => {
    let worker: Worker;
    try {
      worker = new Worker(new URL('./blake3Worker.ts', import.meta.url), { type: 'module' });
    } catch (error) {
      reject(error);
      return;
    }

    const cleanup = () => {
      worker.terminate();
      signal?.removeEventListener('abort', abort);
    };
    const abort = () => {
      cleanup();
      reject(new Error('已取消'));
    };

    worker.onmessage = (event: MessageEvent<{ crc32c?: string; error?: string }>) => {
      if (event.data.crc32c) {
        cleanup();
        resolve(event.data.crc32c);
      } else if (event.data.error) {
        cleanup();
        reject(new Error(event.data.error));
      }
    };
    worker.onerror = (event) => {
      cleanup();
      reject(new Error(event.message || '计算分片 CRC32C 失败'));
    };
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) {
      abort();
      return;
    }

    void chunk
      .arrayBuffer()
      .then((buffer) => {
        if (signal?.aborted) {
          abort();
          return;
        }
        worker.postMessage({ type: 'crc32c', buffer }, [buffer]);
      })
      .catch((error) => {
        cleanup();
        reject(error);
      });
  });
}

/**
 * 在 Worker 中计算单个上传分片的 CRC32C，避免大分片校验占用主线程。
 */
export async function computeChunkCrc32c(chunk: Blob, options: ChunkCrc32cOptions = {}): Promise<string> {
  if (options.signal?.aborted) throw new Error('已取消');
  if (options.useWorker !== false && typeof Worker !== 'undefined') {
    try {
      return await computeChunkCrc32cWithWorker(chunk, options.signal);
    } catch (error) {
      if (options.signal?.aborted || (error as any)?.message === '已取消') throw error;
      // Worker 创建或加载失败时保留兼容回退，避免不支持模块 Worker 的浏览器无法上传。
    }
  }
  return computeChunkCrc32cLocal(chunk);
}

/**
 * 流式计算完整文件 BLAKE3，不会把整个大文件一次性读入内存。
 */
export async function computeFileBlake3(file: File): Promise<string> {
  return computeFileBlake3WithOptions(file);
}

export interface FileBlake3Options {
  useWorker?: boolean;
  signal?: AbortSignal;
  onProgress?: (progress: number) => void;
}

async function computeFileBlake3Local(
  file: File,
  signal?: AbortSignal,
  onProgress?: (progress: number) => void,
): Promise<string> {
  const hasher = blake3.create();
  const blockSize = 4 * 1024 * 1024;
  for (let offset = 0; offset < file.size; offset += blockSize) {
    if (signal?.aborted) throw new Error('已取消');
    const chunk = new Uint8Array(await file.slice(offset, Math.min(file.size, offset + blockSize)).arrayBuffer());
    hasher.update(chunk);
    onProgress?.(file.size === 0 ? 1 : Math.min((offset + chunk.length) / file.size, 1));
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  return toHex(hasher.digest());
}

function computeFileBlake3WithWorker(file: File, options: FileBlake3Options): Promise<string> {
  return new Promise((resolve, reject) => {
    let worker: Worker;
    try {
      worker = new Worker(new URL('./blake3Worker.ts', import.meta.url), { type: 'module' });
    } catch {
      computeFileBlake3Local(file, options.signal, options.onProgress).then(resolve, reject);
      return;
    }

    const cleanup = () => {
      worker.terminate();
      options.signal?.removeEventListener('abort', abort);
    };
    const abort = () => {
      cleanup();
      reject(new Error('已取消'));
    };

    worker.onmessage = (event: MessageEvent<{ blake3?: string; progress?: number; error?: string }>) => {
      if (typeof event.data.progress === 'number') options.onProgress?.(event.data.progress);
      if (event.data.blake3) {
        cleanup();
        resolve(event.data.blake3);
      } else if (event.data.error) {
        cleanup();
        reject(new Error(event.data.error));
      }
    };
    worker.onerror = (event) => {
      cleanup();
      reject(new Error(event.message || '计算文件校验值失败'));
    };
    options.signal?.addEventListener('abort', abort, { once: true });
    if (options.signal?.aborted) {
      abort();
      return;
    }
    worker.postMessage({ type: 'blake3', file });
  });
}

export function computeFileBlake3WithOptions(file: File, options: FileBlake3Options = {}): Promise<string> {
  if (options.signal?.aborted) return Promise.reject(new Error('已取消'));
  if (options.useWorker !== false && typeof Worker !== 'undefined') {
    return computeFileBlake3WithWorker(file, options);
  }
  return computeFileBlake3Local(file, options.signal, options.onProgress);
}
