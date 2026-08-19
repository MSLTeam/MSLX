export interface ResourceSearchFilter {
  query?: string;
  type?: number; // enum: Mod = 0, ResourcePack = 1, DataPack = 2, Shader = 3, Modpack = 4, Plugin = 5
  gameVersion?: string;
  gameLoaders?: string[];
  pluginLoaders?: string[];
  category?: string;
  provider?: number;
  offset?: number;
  limit: number;
}

export interface ResourceModel {
  id: string;
  name: string;
  summary: string;
  description?: string;
  iconUrl?: string;
  author?: string;
  downloadCount: number;
  updatedAt: string;
  provider: number; // enum: Modrinth = 0, CurseForge = 1
}

export interface ResourceVersionModel {
  id: string;
  name: string;
  versionNumber: string;
  downloadUrl: string;
  filename: string;
  fileSizeBytes: number;
  gameVersions?: string[];
  loaders?: string[];
  environment?: number; // 0 = Client, 1 = Server
  dependencies?: ResourceDependencyModel[];
}

export interface ResourceDependencyModel {
  projectId: string;
  versionId?: string;
  name?: string;
  summary?: string;
  iconUrl?: string;
  type: number; // 0=Required, 1=Optional, 2=Incompatible, 3=Embedded
  provider: number; // 0=Modrinth, 1=CurseForge

  // 前端显示状态
  matchStatus?: number; // 0=ExactMatch（单个命中）, 1=MultipleMatches（多个命中）, 2=NotFound（找不到）, 3=Embedded（内嵌了）, 4=AlreadyInstalled（已安装）
  statusMessage?: string;
  selectedVersion?: ResourceVersionModel;
  candidateVersions?: ResourceVersionModel[];
}

export interface ResourceSearchResult {
  items: ResourceModel[];
  totalCount: number;
}
