import { request } from '@/utils/request';
import { PluginListModel } from '@/api/model/plugins';

export async function getPluginList(){
  return await request.get<PluginListModel[]>({
    url: '/api/plugins/list'
  });
}
