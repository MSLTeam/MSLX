import { blake3 } from '@noble/hashes/blake3.js';

// 使用 CRC32C 校验算法，采用反射多项式；所有分片共用一张预先计算好的表～
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

export function toHex(bytes: Uint8Array): string {
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

export async function computeChunkCrc32cLocal(
  chunk: Blob | ArrayBuffer,
  signal?: AbortSignal,
  yieldBetweenBlocks = true,
): Promise<string> {
  const blockSize = 1024 * 1024;
  const size = chunk instanceof Blob ? chunk.size : chunk.byteLength;
  let crc = 0xffffffff;
  for (let offset = 0; offset < size; offset += blockSize) {
    if (signal?.aborted) throw new Error('已取消');
    const length = Math.min(blockSize, size - offset);
    const bytes = chunk instanceof Blob
      ? new Uint8Array(await chunk.slice(offset, offset + length).arrayBuffer())
      : new Uint8Array(chunk, offset, length);
    if (signal?.aborted) throw new Error('已取消');
    for (let index = 0; index < bytes.length; index++) {
      crc = CRC32C_TABLE[(crc ^ bytes[index]) & 0xff] ^ (crc >>> 8);
    }
    if (yieldBetweenBlocks) await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  if (signal?.aborted) throw new Error('已取消');
  return ((crc ^ 0xffffffff) >>> 0).toString(16).padStart(8, '0');
}

export async function computeFileBlake3Local(
  file: File,
  signal?: AbortSignal,
  onProgress?: (_progress: number) => void,
  yieldBetweenBlocks = true,
): Promise<string> {
  const hasher = blake3.create();
  const blockSize = 4 * 1024 * 1024;
  for (let offset = 0; offset < file.size; offset += blockSize) {
    if (signal?.aborted) throw new Error('已取消');
    const chunk = new Uint8Array(await file.slice(offset, Math.min(file.size, offset + blockSize)).arrayBuffer());
    hasher.update(chunk);
    onProgress?.(file.size === 0 ? 1 : Math.min((offset + chunk.length) / file.size, 1));
    if (yieldBetweenBlocks) await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  return toHex(hasher.digest());
}

