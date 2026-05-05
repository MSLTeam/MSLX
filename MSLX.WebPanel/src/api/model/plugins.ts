export interface PluginListModel {
  id: string;
  name: string;
  description: string;
  icon: string;
  version: string;
  minSDKVersion: string;
  developer: string;
  authorUrl: string;
  pluginUrl: string;
  entryPath: string;
}

export interface InstalPluginResponse {
  taskId: string;
}

export interface InstalPluginStatusModel {
  status: string;
  progress: number;
  message: string;
}

// 插件市场
export interface MarketPluginModel {
  appId: string;
  createdAt: string;
  developerAvatar: string;
  developerName: string;
  developerUid: number;
  icon: string;
  name: string;
  shortDesc: string;
  totalDownloads: number;
}

export interface MarketPluginPageModel {
  size: number;
  total: number;
  page: number;
  list: MarketPluginModel[];
}

export interface MarketPluginVersionModel {
  id: number;
  changelog: string;
  createdAt: string;
  downloadCount: number;
  downloadLink: string;
  versionCode: number;
  versionName: string;
}

export interface MarketPluginVersionPageModel {
  size: number;
  total: number;
  page: number;
  list: MarketPluginVersionModel[];
}
