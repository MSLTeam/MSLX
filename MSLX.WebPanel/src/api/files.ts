import { request } from '@/utils/request';
import { UploadFinishResponse, UploadInitResponse } from '@/api/model/files';

export async function initUpload() {
  return await request.post<UploadInitResponse>({
    url: '/api/files/upload/init',
  });
}

export async function uploadChunk(uploadId: string, index: number, file: Blob) {
  const formData = new FormData();
  formData.append('index', index.toString());
  formData.append('file', file);

  return await request.post({
    url: `/api/files/upload/chunk/${uploadId}`,
    data: formData,
    headers: { 'Content-Type': 'multipart/form-data' },
  });
}

export async function finishUpload(uploadId: string, totalChunks: number) {
  return await request.post<UploadFinishResponse>({
    url: `/api/files/upload/finish/${uploadId}`,
    data: { totalChunks },
  });
}

export async function deleteUpload(uploadId: string) {
  return await request.post({
    url: `/api/files/upload/delete/${uploadId}`,
  });
}
