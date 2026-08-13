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
}

export interface ResourceSearchResult {
  items: ResourceModel[];
  totalCount: number;
}
