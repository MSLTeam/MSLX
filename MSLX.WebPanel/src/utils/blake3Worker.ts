import { computeChunkCrc32cLocal, computeFileBlake3Local } from './uploadHash';

const workerScope = globalThis as unknown as {
  onmessage: (event: MessageEvent<{
    type?: 'blake3' | 'crc32c';
    taskId?: number;
    chunk?: Blob;
    file?: File;
    buffer?: ArrayBuffer;
  }>) => void;
  postMessage: (message: {
    taskId?: number;
    blake3?: string;
    crc32c?: string;
    progress?: number;
    error?: string;
  }) => void;
};

workerScope.onmessage = async (event) => {
  try {
    if (event.data.type === 'crc32c') {
      const chunk = event.data.chunk ?? event.data.buffer;
      if (!chunk) throw new Error('缺少待计算的分片');
      const crc32c = await computeChunkCrc32cLocal(chunk, undefined, false);
      workerScope.postMessage({ taskId: event.data.taskId, crc32c });
      return;
    }

    if (!event.data.file) throw new Error('缺少待计算的文件');
    const blake3 = await computeFileBlake3Local(event.data.file, undefined, (progress) =>
      workerScope.postMessage({ progress }), false);
    workerScope.postMessage({ blake3 });
  } catch (error) {
    workerScope.postMessage({
      taskId: event.data.taskId,
      error: error instanceof Error ? error.message : '计算文件校验值失败',
    });
  }
};
