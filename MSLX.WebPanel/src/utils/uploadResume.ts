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
  receivedChunks: number[];
  updatedAt: number;
  fileHandle?: UploadFileHandleLike;
}

const DB_NAME = 'mslx-upload-resume';
const STORE_NAME = 'sessions';
const DB_VERSION = 1;
const HASH_WINDOW_SIZE = 1024 * 1024;

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
  return withStore<UploadResumeRecord[]>('readonly', (store, resolve, reject) => {
    const request = store.getAll();
    request.onsuccess = () => {
      const records = (request.result as UploadResumeRecord[])
        .filter((record) => record.nodeId === nodeId)
        .sort((a, b) => b.updatedAt - a.updatedAt);
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
