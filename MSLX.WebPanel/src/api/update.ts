import { request } from '@/utils/request';
import { UpdateDownloadInfoModel, UpdateInfoModel } from '@/api/model/update';

export async function getDaemonUpdateInfo() {
  return request.get<UpdateInfoModel>({
    url: '/api/update/info',
  });
}

export async function getDaemonUpdateDownloadInfo() {
  return request.get<UpdateDownloadInfoModel>({
    url: '/api/update/download',
  });
}

export async function postUpdateDaemon(){
  return request.post({
    url: '/api/update',
  });
}
