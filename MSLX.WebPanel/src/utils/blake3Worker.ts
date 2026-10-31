import { computeChunkCrc32c, computeFileBlake3WithOptions } from './uploadResume';

const workerScope = globalThis as unknown as {
  onmessage: (event: MessageEvent<{
    type?: 'blake3' | 'crc32c';
    file?: File;
    buffer?: ArrayBuffer;
  }>) => void;
  postMessage: (message: {
    blake3?: string;
    crc32c?: string;
    progress?: number;
    error?: string;
  }) => void;
};

workerScope.onmessage = async (event) => {
  try {
    if (event.data.type === 'crc32c' && event.data.buffer) {
      const crc32c = await computeChunkCrc32c(new Blob([event.data.buffer]), { useWorker: false });
      workerScope.postMessage({ crc32c });
      return;
    }

    if (!event.data.file) throw new Error('缺少待计算的文件');
    const blake3 = await computeFileBlake3WithOptions(event.data.file, {
      useWorker: false,
      onProgress: (progress) => workerScope.postMessage({ progress }),
    });
    workerScope.postMessage({ blake3 });
  } catch (error) {
    workerScope.postMessage({
      error: error instanceof Error ? error.message : '计算文件校验值失败',
    });
  }
};
