import { request } from '@/utils/request';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';

export async function postCreateInstanceQucikMode(data:CreateInstanceQucikModeModel){
  return await request.post({
    url: '/api/instance/createServer',
    data: data,
  });
}
