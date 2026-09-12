import { request } from '@/utils/request';
import { TOKEN_NAME, BASE_URL_NAME } from '@/config/global';
import {
  FilesListModel,
  FilesListResponse,
  HostDriveItem,
  HostFsResponse,
  PluginsAndModsListModel,
  UploadFinishResponse,
  UploadInitResponse,
  UploadPackageCheckJarResponse,
} from '@/api/model/files';
import { AxiosProgressEvent } from 'axios';

export async function initUpload() {
  return await request.post<UploadInitResponse>({
    url: '/api/files/upload/init',
  });
}

export async function uploadChunk(
  uploadId: string,
  index: number,
  file: Blob,
  onProgress?: (_e: AxiosProgressEvent) => void,
  signal?: AbortSignal,
) {
  const formData = new FormData();
  formData.append('index', index.toString());
  formData.append('file', file);

  return await request.post({
    url: `/api/files/upload/chunk/${uploadId}`,
    data: formData,
    headers: { 'Content-Type': 'multipart/form-data' },
    timeout: 300 * 1000,
    onUploadProgress: onProgress,
    signal,
  });
}

export async function finishUpload(uploadId: string, totalChunks: number) {
  return await request.post<UploadFinishResponse>({
    url: `/api/files/upload/finish/${uploadId}`,
    data: { totalChunks },
    timeout: 120 * 1000,
  });
}

export async function deleteUpload(uploadId: string) {
  return await request.post({
    url: `/api/files/upload/delete/${uploadId}`,
  });
}

export async function checkPackageJarList(uploadId: string, localPath?: string) {
  const id = localPath ? '0' : uploadId;

  return await request.get<UploadPackageCheckJarResponse>({
    url: `/api/files/upload/inspect/${id}`,
    params: localPath ? { localPath } : undefined,
    timeout: 60 * 1000,
  });
}

export interface FileListParams {
  path?: string;
  page?: number;
  pageSize?: number;
  search?: string;
  sort?: string;
  order?: 'asc' | 'desc';
}

// 文件管理系统
export async function getInstanceFilesList(instanceId: number, params?: FileListParams | string) {
  const queryParams = typeof params === 'string' ? { path: params } : (params || {});
  return await request.get<FilesListResponse | FilesListModel[]>({
    url: `/api/files/instance/${instanceId}/lists`,
    params: queryParams,
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

export function createDirectory(instanceId: number, path: string,name: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/directory`,
    data: { path, name }
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

export function getVideoStreamUrl(instanceId: number, path: string): string {
  const token = localStorage.getItem(TOKEN_NAME);
  const baseUrl = localStorage.getItem(BASE_URL_NAME);
  const activeNodeId = localStorage.getItem('ACTIVE_NODE_ID');
  const activeNodeUrl = localStorage.getItem('ACTIVE_NODE_URL');

  let finalBase = baseUrl || window.location.origin;
  if (activeNodeUrl && activeNodeId !== 'local') {
    finalBase = activeNodeUrl;
  }

  const url = new URL(`/api/files/instance/${instanceId}/download`, finalBase);
  url.searchParams.append('path', path);
  url.searchParams.append('inline', 'true');
  if (token) {
    url.searchParams.append('x-user-token', token);
  }
  if (activeNodeId && activeNodeId !== 'local') {
    url.searchParams.append('x-node-id', activeNodeId);
  }
  return url.toString();
}

export function getFileThumbnailUrl(instanceId: number, path: string, size = 256): string {
  const token = localStorage.getItem(TOKEN_NAME);
  const baseUrl = localStorage.getItem(BASE_URL_NAME);
  const activeNodeId = localStorage.getItem('ACTIVE_NODE_ID');
  const activeNodeUrl = localStorage.getItem('ACTIVE_NODE_URL');

  let finalBase = baseUrl || window.location.origin;
  if (activeNodeUrl && activeNodeId !== 'local') {
    finalBase = activeNodeUrl;
  }

  const url = new URL(`/api/files/instance/${instanceId}/thumbnail`, finalBase);
  url.searchParams.append('path', path);
  url.searchParams.append('size', size.toString());
  if (token) {
    url.searchParams.append('x-user-token', token);
  }
  if (activeNodeId && activeNodeId !== 'local') {
    url.searchParams.append('x-node-id', activeNodeId);
  }
  return url.toString();
}

export function startCompress(instanceId: number, sources: string[], targetName: string, currentPath: string, password?: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/compress`,
    data: { sources, targetName, currentPath, password: password || undefined }
  });
}

export function getCompressStatus(taskId: string) {
  return request.get<{ status: string; progress: number; message: string }>({
    url: `/api/files/task/compress/${taskId}`
  });
}

export function startDecompress(instanceId: number, fileName: string, currentPath: string, encoding = 'utf-8', createSubFolder: boolean = true, password?: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/decompress`,
    data: { fileName, currentPath, encoding, createSubFolder, password: password || undefined }
  });
}

export function getDeompressStatus(taskId: string) {
  return request.get<{ status: string; progress: number; message: string }>({
    url: `/api/files/task/decompress/${taskId}`
  });
}


export async function changeFileMode(instanceId: number, path: string, mode: string) {
  return await request.post({
    url: `/api/files/instance/${instanceId}/chmod`,
    data: { path, mode }
  });
}

// 模组/插件管理

export async function getPluginsOrModsList(instanceId: number, type: 'mods' | 'plugins',checkClient: boolean){
  return await request.get<PluginsAndModsListModel>({
    url: `/api/files/pm/instance/${instanceId}/list?${checkClient?'checkClient=true':''}`,
    params: { mode: type }
  });
}

export async function setPluginsOrModsStatus(instanceId:number, type: 'mods' | 'plugins',action: 'enable' | 'disable' | 'delete', targets: string[] ){
  return await request.post({
    url: `/api/files/pm/instance/${instanceId}/set`,
    data: { mode: type, action, targets }
  });
}

// 批量复制
export function copyFiles(instanceId: number, sourcePaths: string[], targetPath: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/copy`,
    data: { sourcePaths, targetPath },
    timeout: 120 * 1000,
  });
}

// 批量移动
export function moveFiles(instanceId: number, sourcePaths: string[], targetPath: string) {
  return request.post({
    url: `/api/files/instance/${instanceId}/move`,
    data: { sourcePaths, targetPath },
    timeout: 120 * 1000,
  });
}

// 上传图片到静态资源文件夹
export function uploadFilesToStaticImages(fileKey: string,fileName: string) {
  return request.post({
    url: `/api/static/images/upload`,
    data: { fileKey, fileName },
  });
}

// 离线下载相关
export async function addOfflineDownloadTask(instanceId: number, path: string, url: string,filename: string) {
  return await request.post({
    url: `/api/files/instance/${instanceId}/download`,
    data: { path, url, filename },
  });
}

export function getOfflineDownloadTaskStatus(taskId: string) {
  return request.get<{ status: string; progress: number; message: string }>({
    url: `/api/files/task/download/${taskId}`,
  });
}

// host fs
export async function getHostFilesList(path?: string, searchPattern?: string) {
  return await request.get<HostFsResponse>({
    url: '/api/files/root',
    params: {
      path: path || undefined,
      searchPattern: searchPattern || undefined,
    },
  });
}

export async function getHostDrivesList() {
  return await request.get<HostDriveItem[]>({
    url: '/api/files/drives',
  });
}


// 后台任务管理
export async function getUserTasks(instanceId?: number) {
  return await request.get<any[]>({ url: '/api/tasks', params: { instanceId } });
}

export async function cancelUserTask(taskId: string) {
  return await request.post({ url: `/api/tasks/${taskId}/cancel` });
}

export async function deleteUserTask(taskId: string) {
  return await request.delete({ url: `/api/tasks/${taskId}` });
}

export async function clearFinishedUserTasks() {
  return await request.post({ url: '/api/tasks/clear-finished' });
}
