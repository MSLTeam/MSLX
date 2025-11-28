import { request } from '@/utils/request';
import { FrpListModel, TunnelInfoModel } from '@/api/model/frp';

export async  function postCreateFrpTunnel(name:string,config:string,provider:string,format:string = 'toml'){
  return await request.post({
    url: '/api/frp/add',
    data:{
      name,
      config,
      provider,
      format
    }
  });
}

export async function postDeleteFrpTunnel(id:number){
  return await request.post({
    url: '/api/frp/delete',
    data:{
      id
    }
  });
}

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

export function getTunnelInfo(id:number){
  return request.get<TunnelInfoModel>({
    url: '/api/frp/info',
    params:{
      id
    }
  });
}
