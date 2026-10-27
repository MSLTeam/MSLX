export enum PluginMultiNodeSupport {
  NoTest = 'no-test', Supported = 'true', Unsupported = 'false',
}

export function parsePluginMultiNodeSupport(value: unknown): PluginMultiNodeSupport {
  if (value === undefined) return PluginMultiNodeSupport.NoTest;
  if (!Object.values(PluginMultiNodeSupport).includes(value as PluginMultiNodeSupport))
    throw new Error('插件多节点支持声明无效');
  return value as PluginMultiNodeSupport;
}

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
  status: string;
  supportsMultiNode?: PluginMultiNodeSupport;
  frontendRevision?: string;
  syncInfo?: any;
  hasPublicPages?: boolean;
  isPublicAccessEnabled?: boolean;
  categoryList?: PluginCategoryModel[];
}

export interface InstalPluginResponse {
  taskId: string;
}

export interface InstalPluginStatusModel {
  status: string;
  progress: number;
  message: string;
}

export interface PluginCategoryModel {
  id: number;
  name: string;
  sortOrder: number;
  createTime: string;
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
  categoryList?: PluginCategoryModel[];
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
  minSdkVersion: string;
}

export interface MarketPluginVersionPageModel {
  size: number;
  total: number;
  page: number;
  list: MarketPluginVersionModel[];
}
