import { request } from '@/utils/request';
import { CreateInstanceQucikModeModel, InstanceInfoModel, InstanceListModel } from '@/api/model/instance';

export async function postCreateInstanceQucikMode(data:CreateInstanceQucikModeModel){
  return await request.post({
    url: '/api/instance/createServer',
    data: data,
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
    }
  });
}

export async function getInstanceInfo(id:number){
  return await request.get<InstanceInfoModel>({
    url: '/api/instance/info',
    params:{
      id: id
    },
    timeout: 60 * 1000, // 可能存在小聪明开了又关 导致关闭操作耗时。
  });
}
