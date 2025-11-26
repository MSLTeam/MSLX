import { request } from '@/utils/request';
import { FrpListModel } from '@/api/model/frp';

export function getFrpList() {
  return request.get<FrpListModel[]>({
    url: '/api/frp/list',
  });
}

export function postFrpAction(action:string , id:number){
  return request.post({
    url: '/api/frp/action',
    data:{
      action,
      id
    }
  });
}
