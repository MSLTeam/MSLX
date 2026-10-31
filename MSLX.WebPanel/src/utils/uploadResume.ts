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
  sha256?: string;
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

  if (globalThis.crypto?.subtle) {
    const digest = await globalThis.crypto.subtle.digest('SHA-256', input);
    return `head-tail-sha256:${file.size}:${toHex(new Uint8Array(digest))}`;
  }

  // 非安全上下文的兼容回退：仍只扫描首尾窗口，不读取整个文件。
  let hash = 2166136261;
  for (const byte of input) {
    hash ^= byte;
    hash = Math.imul(hash, 16777619);
  }
  return `head-tail-fnv1a:${file.size}:${(hash >>> 0).toString(16).padStart(8, '0')}`;
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
      worker = new Worker(new URL('./sha256Worker.ts', import.meta.url), { type: 'module' });
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

const SHA256_K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

function rightRotate(value: number, bits: number): number {
  return (value >>> bits) | (value << (32 - bits));
}

class IncrementalSha256 {
  private state = new Uint32Array([
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
  ]);
  private buffer = new Uint8Array(64);
  private bufferLength = 0;
  private bytesHashed = 0;

  update(data: Uint8Array): void {
    this.bytesHashed += data.length;
    let offset = 0;

    if (this.bufferLength > 0) {
      const copied = Math.min(64 - this.bufferLength, data.length);
      this.buffer.set(data.subarray(0, copied), this.bufferLength);
      this.bufferLength += copied;
      offset += copied;
      if (this.bufferLength === 64) {
        this.processBlock(this.buffer);
        this.bufferLength = 0;
      }
    }

    while (offset + 64 <= data.length) {
      this.processBlock(data.subarray(offset, offset + 64));
      offset += 64;
    }

    if (offset < data.length) {
      this.buffer.set(data.subarray(offset), 0);
      this.bufferLength = data.length - offset;
    }
  }

  digest(): string {
    const bitLength = this.bytesHashed * 8;
    const paddingLength = this.bufferLength < 56 ? 56 - this.bufferLength : 120 - this.bufferLength;
    const padding = new Uint8Array(paddingLength);
    padding[0] = 0x80;
    this.update(padding);

    const length = new Uint8Array(8);
    const high = Math.floor(bitLength / 0x100000000);
    const low = bitLength >>> 0;
    length[0] = (high >>> 24) & 0xff;
    length[1] = (high >>> 16) & 0xff;
    length[2] = (high >>> 8) & 0xff;
    length[3] = high & 0xff;
    length[4] = (low >>> 24) & 0xff;
    length[5] = (low >>> 16) & 0xff;
    length[6] = (low >>> 8) & 0xff;
    length[7] = low & 0xff;
    this.update(length);

    const output = new Uint8Array(32);
    for (let i = 0; i < this.state.length; i++) {
      output[i * 4] = (this.state[i] >>> 24) & 0xff;
      output[i * 4 + 1] = (this.state[i] >>> 16) & 0xff;
      output[i * 4 + 2] = (this.state[i] >>> 8) & 0xff;
      output[i * 4 + 3] = this.state[i] & 0xff;
    }
    return toHex(output);
  }

  private processBlock(block: Uint8Array): void {
    const words = new Uint32Array(64);
    for (let i = 0; i < 16; i++) {
      const offset = i * 4;
      words[i] = ((block[offset] << 24) | (block[offset + 1] << 16) |
        (block[offset + 2] << 8) | block[offset + 3]) >>> 0;
    }
    for (let i = 16; i < 64; i++) {
      const s0 = rightRotate(words[i - 15], 7) ^ rightRotate(words[i - 15], 18) ^ (words[i - 15] >>> 3);
      const s1 = rightRotate(words[i - 2], 17) ^ rightRotate(words[i - 2], 19) ^ (words[i - 2] >>> 10);
      words[i] = (words[i - 16] + s0 + words[i - 7] + s1) >>> 0;
    }

    let [a, b, c, d, e, f, g, h] = this.state;
    for (let i = 0; i < 64; i++) {
      const sum1 = rightRotate(e, 6) ^ rightRotate(e, 11) ^ rightRotate(e, 25);
      const choice = (e & f) ^ (~e & g);
      const temp1 = (h + sum1 + choice + SHA256_K[i] + words[i]) >>> 0;
      const sum0 = rightRotate(a, 2) ^ rightRotate(a, 13) ^ rightRotate(a, 22);
      const majority = (a & b) ^ (a & c) ^ (b & c);
      const temp2 = (sum0 + majority) >>> 0;
      h = g;
      g = f;
      f = e;
      e = (d + temp1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temp1 + temp2) >>> 0;
    }

    this.state[0] = (this.state[0] + a) >>> 0;
    this.state[1] = (this.state[1] + b) >>> 0;
    this.state[2] = (this.state[2] + c) >>> 0;
    this.state[3] = (this.state[3] + d) >>> 0;
    this.state[4] = (this.state[4] + e) >>> 0;
    this.state[5] = (this.state[5] + f) >>> 0;
    this.state[6] = (this.state[6] + g) >>> 0;
    this.state[7] = (this.state[7] + h) >>> 0;
  }
}

/**
 * 流式计算完整文件 SHA-256，不会把整个大文件一次性读入内存。
 */
export async function computeFileSha256(file: File): Promise<string> {
  return computeFileSha256WithOptions(file);
}

export interface FileSha256Options {
  useWorker?: boolean;
  signal?: AbortSignal;
  onProgress?: (progress: number) => void;
}

async function computeFileSha256Local(
  file: File,
  signal?: AbortSignal,
  onProgress?: (progress: number) => void,
): Promise<string> {
  const hasher = new IncrementalSha256();
  const blockSize = 4 * 1024 * 1024;
  for (let offset = 0; offset < file.size; offset += blockSize) {
    if (signal?.aborted) throw new Error('已取消');
    const chunk = new Uint8Array(await file.slice(offset, Math.min(file.size, offset + blockSize)).arrayBuffer());
    hasher.update(chunk);
    onProgress?.(file.size === 0 ? 1 : Math.min((offset + chunk.length) / file.size, 1));
    // 让出主线程，避免不支持 Worker 的浏览器完全阻塞页面。
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  return hasher.digest();
}

function computeFileSha256WithWorker(file: File, options: FileSha256Options): Promise<string> {
  return new Promise((resolve, reject) => {
    let worker: Worker;
    try {
      worker = new Worker(new URL('./sha256Worker.ts', import.meta.url), { type: 'module' });
    } catch {
      computeFileSha256Local(file, options.signal, options.onProgress).then(resolve, reject);
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

    worker.onmessage = (event: MessageEvent<{ sha256?: string; progress?: number; error?: string }>) => {
      if (typeof event.data.progress === 'number') options.onProgress?.(event.data.progress);
      if (event.data.sha256) {
        cleanup();
        resolve(event.data.sha256);
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
    worker.postMessage({ type: 'sha256', file });
  });
}

export function computeFileSha256WithOptions(file: File, options: FileSha256Options = {}): Promise<string> {
  if (options.signal?.aborted) return Promise.reject(new Error('已取消'));
  if (options.useWorker !== false && typeof Worker !== 'undefined') {
    return computeFileSha256WithWorker(file, options);
  }
  return computeFileSha256Local(file, options.signal, options.onProgress);
}
