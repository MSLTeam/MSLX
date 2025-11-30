export interface ServerCoreClassifyModel {
  pluginsCore: string[];
  pluginsAndModsCore_Forge: string[];
  pluginsAndModsCore_Fabric: string[];
  modsCore_Forge: string[];
  modsCore_Fabric: string[];
  vanillaCore: string[];
  bedrockCore: string[];
  proxyCore: string[];
}

export interface ServerCoreGameVersionModel{
  versionList: string[];
}

export interface ServerCoreDownloadInfoModel{
  url: string;
  sha256?: string;
}
