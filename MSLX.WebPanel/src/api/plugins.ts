import { request } from '@/utils/request';
import { getPageNodeId, getNodeTarget } from '@/utils/nodeTarget';
import { parsePluginMultiNodeSupport } from '@/api/model/plugins';
import {
  InstalPluginResponse,
  InstalPluginStatusModel,
  MarketPluginPageModel,
  MarketPluginVersionModel,
  MarketPluginVersionPageModel,
  PluginListModel,
} from '@/api/model/plugins';

export async function getPluginList(params?: { categoryId?: number; includeSyncInfo?: boolean }, nodeId = getPageNodeId()){
  const list = await request.get<PluginListModel[]>({
    url: '/api/plugins/list',
    params
  }, { targetNodeId: nodeId });
  return list.map(plugin => ({ ...plugin, supportsMultiNode: parsePluginMultiNodeSupport(plugin.supportsMultiNode),
    icon: plugin.icon?.startsWith('/') ? new URL(plugin.icon, getNodeTarget(nodeId).baseUrl).toString() : plugin.icon }));
}

export async function postPluginAction(id: string, action: string, nodeId = getPageNodeId()) {
  const result = await request.post({ url: '/api/plugins/action', data: { id, action } }, { targetNodeId: nodeId });
  (await import('@/utils/pluginManager')).invalidateNodePlugins(nodeId);
  return result;
}

export async function postInstallPlugin(downloadUrl: string, fileName: string, overwrite = true, nodeId = getPageNodeId()) {
  return request.post<InstalPluginResponse>({ url: '/api/plugins/install', data: { downloadUrl, fileName, overwrite } }, { targetNodeId: nodeId });
}

export async function getInstallPluginStatus(taskId: string, nodeId = getPageNodeId()) {
  const result = await request.get<InstalPluginStatusModel>({ url: `/api/plugins/task/install/${taskId}` }, { targetNodeId: nodeId });
  if (result.status === 'success') (await import('@/utils/pluginManager')).invalidateNodePlugins(nodeId);
  return result;
}

export async function postUploadLocalPlugin(fileId: string, fileName?: string, nodeId = getPageNodeId()) {
  const result = await request.post({ url: '/api/plugins/upload', data: { fileId, fileName } }, { targetNodeId: nodeId });
  (await import('@/utils/pluginManager')).invalidateNodePlugins(nodeId);
  return result;
}

// 插件市场
export async function getMarketCategories() {
  return await request.get<import('@/api/model/plugins').PluginCategoryModel[]>({
    url: 'https://mslx-plugins-api.mslmc.net/api/categories/list',
  });
}

export async function getMarketPluginList(params: { keyword?: string; page?: number; size?: number; categoryId?: number }) {
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

// 插件检查更新接口
export async function getPluginUpdates(plugins: string) {
  return await request.get<(MarketPluginVersionModel & { appId: string })[]>({
    url: 'https://mslx-plugins-api.mslmc.net/api/plugins/versions/check-updates',
    params: { plugins },
  });
}
