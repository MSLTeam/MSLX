import { request } from '@/utils/request';
import { CreateInstanceQucikModeModel, InstanceListModel } from '@/api/model/instance';

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
