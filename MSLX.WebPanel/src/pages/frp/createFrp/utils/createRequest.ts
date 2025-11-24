import { request } from '@/utils/request';

export async  function  createFrpTunnel(name:string,config:string,provider:string,format:string = 'toml'){
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
