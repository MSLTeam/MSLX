import { request } from '@/utils/request';
import {
  AllInstanceBackupFilesModel,
  CreateInstanceQucikModeModel,
  InstanceBackupFilesModel,
  InstanceInfoModel,
  InstanceListModel,
  InstanceSettingsModel,
  UpdateInstanceResponseModel,
} from '@/api/model/instance';
import { useUserStore } from '@/store';

const userStore = useUserStore();

export async function postCreateInstanceQuickMode(data:CreateInstanceQucikModeModel){
  return await request.post({
    url: '/api/instance/createServer',
    data: data,
  });
}

export async function postDeleteInstance(id:number,deleteFiles:boolean = false) {
  return await request.post({
    url: '/api/instance/delete',
    data:{
      id,
      deleteFiles
    }
  });
}

export async function getInstanceList() {
  return await request.get<InstanceListModel[]>({
    url: '/api/instance/list',
  });
}

export async function postInstanceAction(id:number, action:string){
  return await request.post({
    url: '/api/instance/action',
    data:{
      id: id,
      action: action
    },
    timeout: 60 * 1000, // 可能存在小聪明开了又关 导致关闭操作耗时。
  });
}

export async function getInstanceInfo(id:number){
  return await request.get<InstanceInfoModel>({
    url: '/api/instance/info',
    params:{
      id: id
    }
  });
}

export async function getInstanceSettings(id:number){
  return await request.get<InstanceSettingsModel>({
    url: `/api/instance/settings/general/${id}`,
  });
}

export async function postInstanceSettings(data:InstanceSettingsModel){
  return await request.post<UpdateInstanceResponseModel>({
    url: `/api/instance/settings/general/${data.id}`,
    data: data
  });
}

export async function getInstanceBackupFiles(id:number){
  return await request.get<InstanceBackupFilesModel[]>({
    url: `/api/instance/backups/${id}`,
  })
}

export async function getAllInstanceBackupFiles() {
  return await request.get<AllInstanceBackupFilesModel[]>({
    url: `/api/instance/backups/all`,
  });
}

export async function postDeleteBackupFiles(id:number,fileName:string){
  return await request.post({
    url: `/api/instance/backups/delete`,
    data: {id,fileName},
  })
}
export function getBackupDownloadUrl(id: number, fileName: string) {
  const { baseUrl, token } = userStore;
  return `${baseUrl || window.location.origin}/api/instance/backups/download?id=${id}&fileName=${encodeURIComponent(fileName)}&x-user-token=${token}`;
}

