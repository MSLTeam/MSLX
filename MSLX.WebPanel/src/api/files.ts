import { request } from '@/utils/request';
import {
  FilesListModel,
  UploadFinishResponse,
  UploadInitResponse,
  UploadPackageCheckJarResponse,
} from '@/api/model/files';

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
    timeout: 120 * 1000, // 120秒够上传5mb了吧
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

export async function checkPackageJarList(uploadId: string){
  return await request.get<UploadPackageCheckJarResponse>({
    url: `/api/files/upload/inspect/${uploadId}`
  });
}

// 文件管理系统
export async function getInstanceFilesList(instanceId: number,path: string = ''){
  return await request.get<FilesListModel[]>({
    url: `/api/files/instance/${instanceId}/lists`,
    params: { path }
  });
}

export async function getFileContent(instanceId: number, path: string){
  return await request.get<string>({
    url: `/api/files/instance/${instanceId}/content`,
    params: { path }
  });
}

export function saveFileContent(instanceId: number, path: string, content: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/content`,
    data: { path, content }
  });
}
