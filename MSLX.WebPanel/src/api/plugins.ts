import { request } from '@/utils/request';
import {
  InstalPluginResponse,
  InstalPluginStatusModel,
  MarketPluginPageModel,
  MarketPluginVersionPageModel,
  PluginListModel,
} from '@/api/model/plugins';

export async function getPluginList(){
  return await request.get<PluginListModel[]>({
    url: '/api/plugins/list'
  });
}

export async function postPluginAction(id:string, action:string){
  return await request.post({
    url: `/api/plugins/action`,
    data: {id, action}
  });
}

export async function postInstallPlugin(downloadUrl: string, fileName: string, overwrite: boolean = true) {
  return await request.post<InstalPluginResponse>({
    url: '/api/plugins/install',
    data: {downloadUrl, fileName,overwrite},
  });
}

export async function getInstallPluginStatus(taskId:string) {
  return await request.get<InstalPluginStatusModel>({
    url:`/api/plugins/task/install/${taskId}`,
  })
}

// 插件市场
export async function getMarketPluginList(params: { keyword?: string; page?: number; size?: number }) {
  return await request.get<MarketPluginPageModel>({
    url: 'https://mslx-plugins-api.mslmc.net/api/plugins/list',
    params,
  });
}


export async function getMarketPluginVersions(appId: string, params: { page?: number; size?: number }) {
  return await request.get<MarketPluginVersionPageModel>({
    url: `https://mslx-plugins-api.mslmc.net/api/plugins/versions/list/${appId}`,
    params,
  });
}
