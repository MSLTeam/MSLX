import { computeChunkCrc32c, computeFileSha256WithOptions } from './uploadResume';

const workerScope = globalThis as unknown as {
  onmessage: (event: MessageEvent<{ type?: 'sha256' | 'crc32c'; file?: File; buffer?: ArrayBuffer }>) => void;
  postMessage: (message: { sha256?: string; crc32c?: string; progress?: number; error?: string }) => void;
};

workerScope.onmessage = async (event) => {
  try {
    if (event.data.type === 'crc32c' && event.data.buffer) {
      const crc32c = await computeChunkCrc32c(new Blob([event.data.buffer]), { useWorker: false });
      workerScope.postMessage({ crc32c });
      return;
    }

    if (!event.data.file) throw new Error('缺少待计算的文件');
    const sha256 = await computeFileSha256WithOptions(event.data.file, {
      useWorker: false,
      onProgress: (progress) => workerScope.postMessage({ progress }),
    });
    workerScope.postMessage({ sha256 });
  } catch (error: any) {
    workerScope.postMessage({ error: error?.message || '计算文件校验值失败' });
  }
};
