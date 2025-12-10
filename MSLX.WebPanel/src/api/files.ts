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

export function renameFile(instanceId: number, oldPath: string, newPath: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/rename`,
    data: { oldPath, newPath }
  });
}

export function deleteFiles(instanceId: number, paths: string[]) {
  return request.post({
    url: `/api/files/instance/${instanceId}/delete`,
    data: { paths }
  });
}

export function saveUploadedFile(instanceId: number, uploadId: string, fileName: string, currentPath: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/upload`,
    data: { uploadId, fileName, currentPath }
  });
}

export function downloadFileStream(instanceId: number, path: string) {
  return request.get({
    url: `/api/files/instance/${instanceId}/download`,
    params: { path },
    responseType: 'blob',
  });
}

export function startCompress(instanceId: number, sources: string[], targetName: string, currentPath: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/compress`,
    data: { sources, targetName, currentPath }
  });
}

export function getCompressStatus(taskId: string) {
  return request.get<{ status: string; progress: number; message: string }>({
    url: `/api/files/task/compress/${taskId}`
  });
}

export function startDecompress(instanceId: number, fileName: string, currentPath: string,encoding = 'utf-8',createSubFolder: boolean =  true) {
  return request.post({
    url: `/api/files/instance/${instanceId}/decompress`,
    data: {  fileName, currentPath,encoding,createSubFolder }
  });
}

export function getDeompressStatus(taskId: string) {
  return request.get<{ status: string; progress: number; message: string }>({
    url: `/api/files/task/decompress/${taskId}`
  });
}
