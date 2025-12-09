import { request } from '@/utils/request';
import {
  CreateInstanceQucikModeModel,
  InstanceInfoModel,
  InstanceListModel,
  InstanceSettingsModel,
  UpdateInstanceResponseModel,
} from '@/api/model/instance';

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
