import { request } from '@/utils/request';
import { UpdateDownloadInfoModel, UpdateInfoModel } from '@/api/model/update';

export async function getDaemonUpdateInfo(nodeId?: string, nodeUrl?: string) {
  return request.get<UpdateInfoModel>({
    url: '/api/update/info',
    baseURL: nodeUrl || undefined,
    headers: nodeId && nodeId !== 'local' ? { 'x-node-id': nodeId } : {}
  });
}

export async function getDaemonStatusInfo(nodeId?: string, nodeUrl?: string) {
  return request.get<any>({
    url: '/api/status',
    baseURL: nodeUrl || undefined,
    headers: nodeId && nodeId !== 'local' ? { 'x-node-id': nodeId } : {}
  });
}

export async function getDaemonUpdateDownloadInfo(nodeId?: string, nodeUrl?: string) {
  return request.get<UpdateDownloadInfoModel>({
    url: '/api/update/download',
    baseURL: nodeUrl || undefined,
    headers: nodeId && nodeId !== 'local' ? { 'x-node-id': nodeId } : {}
  });
}

export async function postUpdateDaemon(nodeId?: string, nodeUrl?: string){
  return request.post({
    url: '/api/update',
    baseURL: nodeUrl || undefined,
    headers: nodeId && nodeId !== 'local' ? { 'x-node-id': nodeId } : {}
  });
}
