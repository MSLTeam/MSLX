import { request } from '@/utils/request';
import { PluginListModel } from '@/api/model/plugins';

export async function getPluginList(){
  return await request.get<PluginListModel[]>({
    url: '/api/plugins/list'
  });
}

export async function postPluginAction(id:string, action:string){
  return await request.post({
    url: '/api/plugins/action',
    data:{
      id,
      action
    }
  });
}
