export interface CreateInstanceQucikModeModel {
  name: string;
  path: string;
  java: string;
  core: string;
  packageFileKey: string;
  packageLocalPath: string;
  coreFileKey: string;
  coreUrl: string;
  coreSha256: string;
  minM: number;
  maxM: number;
  args: string;
  ignoreEula?: boolean;

  dockerImage?: string;
  dockerPorts?: string;

  // MCDReforged 模式
  mcdr?: boolean;
  mcdrPython?: string;
  mcdrHandler?: string;
  mcdrInstall?: boolean;
  mcdrPipMirror?: string;
}

export interface PythonInfoModel {
  path: string;
  version: string;
  hasMcdr: boolean;
  mcdrVersion?: string;
}

export interface InstanceListModel {
  id: number;
  name: string;
  basePath: string;
  java: string;
  core: string;
  icon: string;
  status: number;
  extra: {
    onlinePlayers: number;
  }
}

export interface InstanceInfoModel {
  id: number;
  name: string;
  basePath: string;
  java: string;
  args: string;
  core: string;
  minM: number;
  maxM: number;
  status: number;
  uptime: string;
  monitorPlayers: boolean;
  mcConfig: {
    difficulty: string;
    levelName: string;
    gamemode: string;
    serverPort: string;
    onlineMode: string;
    serverPropertiesPath?: string;
    serverPropertiesExists?: boolean;
  };
}

export interface UpdateInstanceModel {
  id: number;
  name: string;
  base: string;
  java: string;
  core: string;
  minM: number;
  maxM: number;
  args?: string;
  // 这些后端有默认值
  forceExitDelay?: number;
  yggdrasilApiAddr?: string;
  stopCommand?: string;
  backupMaxCount?: number;
  backupDelay?: number;
  backupPath?: string;
  monitorPlayers?: boolean;
  autoRestart?: boolean;
  forceAutoRestart?: boolean;
  allowOriginASCIIColors?: boolean;
  ignoreEula?: boolean;
  forceJvmUTF8?: boolean;
  runOnStartup?: boolean;
  inputEncoding?: 'utf-8' | 'gbk';
  outputEncoding?: 'utf-8' | 'gbk';
  fileEncoding?: 'utf-8' | 'utf-8-bom' | 'gbk';
  serverPropertiesPath?: string;
  pluginsPath?: string;
  modsPath?: string;
  worldPath?: string;
  regionPath?: string;
  expireTime?: string;

  // ====== Docker 配置字段 ======
  dockerImage: string;
  dockerWorkingDir: string;
  dockerVolumes?: string; // 格式: "/宿主机路径:/容器内路径"
  dockerEnvVars?: string; // 格式: "KEY=VALUE"
  dockerNetworkMode?: string; // "bridge" | "host" | "none" 等
  dockerNetworkAlias?: string; // 仅在自定义网桥下使用
  dockerPorts?: string; // 格式: "0" 或 "宿主机端口:容器端口"

  // 资源与硬件隔离 (Cgroups)
  dockerCpuPercentage?: number; // 范围: 1 - 100
  dockerCpuCores?: string; // 格式: "0" | "0,1" | "0-3"
  dockerMaxMemoryMb?: number; // 最大内存限制
  dockerMaxSwapMb?: number; // 最大交换内存限制
  dockerMaxStorage?: string; // 仅Linux，格式: "10g" | "500m"

  // 网络吞吐速率限制
  dockerUploadRate?: string; // 格式: "1mb" | "500kb"
  dockerDownloadRate?: string; // 格式: "1mb"

  dockerExtraArgs?: string; // 额外透传的 docker run 原生参数
  dockerExtraHosts?: string; // 额外 Hosts 映射，格式: "host.mslx.internal:host-gateway"

  // 可选
  coreFileKey?: string;
  coreUrl?: string;
  coreSha256?: string;
}

export interface InstanceSettingsModel {
  id: number;
  name: string;
  base: string;
  java: string;
  core: string;
  minM: number;
  maxM: number;
  args?: string;
  stopCommand?: string;
  yggdrasilApiAddr?: string;
  backupMaxCount?: number;
  backupDelay?: number;
  backupPath?: string;
  autoRestart?: boolean;
  allowOriginASCIIColors?: boolean;
  forceAutoRestart?: boolean;
  ignoreEula?: boolean;
  forceJvmUTF8?: boolean;
  runOnStartup?: boolean;
  inputEncoding?: 'utf-8' | 'gbk';
  outputEncoding?: 'utf-8' | 'gbk';
  fileEncoding?: 'utf-8' | 'utf-8-bom' | 'gbk';
  serverPropertiesPath?: string;
  pluginsPath?: string;
  modsPath?: string;
  worldPath?: string;
  regionPath?: string;
  // ====== Docker 配置字段 ======
  dockerImage: string;
  dockerWorkingDir: string;
  dockerVolumes?: string; // 格式: "/宿主机路径:/容器内路径"
  dockerEnvVars?: string; // 格式: "KEY=VALUE"
  dockerNetworkMode?: string; // "bridge" | "host" | "none" 等
  dockerNetworkAlias?: string; // 仅在自定义网桥下使用
  dockerPorts?: string; // 格式: "0" 或 "宿主机端口:容器端口"

  // 资源与硬件隔离 (Cgroups)
  dockerCpuPercentage?: number; // 范围: 1 - 100
  dockerCpuCores?: string; // 格式: "0" | "0,1" | "0-3"
  dockerMaxMemoryMb?: number; // 最大内存限制
  dockerMaxSwapMb?: number; // 最大交换内存限制
  dockerMaxStorage?: string; // 仅Linux，格式: "10g" | "500m"

  // 网络吞吐速率限制
  dockerUploadRate?: string; // 格式: "1mb" | "500kb"
  dockerDownloadRate?: string; // 格式: "1mb"

  dockerExtraArgs?: string; // 额外透传的 docker run 原生参数
  dockerExtraHosts?: string; // 额外 Hosts 映射，格式: "host.mslx.internal:host-gateway"
}

export interface UpdateInstanceResponseModel {
  needListen: boolean;
}

export interface InstanceBackupFilesModel {
  fileName: string;
  fileSize: number;
  fileSizeStr: string;
  createTime: string;
  timestamp: string;
}

export interface AllInstanceBackupFilesModel{
  id: number;
  name: string;
  core: string;
  backupPath: string;
  backups: InstanceBackupFilesModel[];
}

// 玩家管理相关
export interface WhitelistItem {
  uuid: string;
  name: string;
}

export interface OpItem {
  uuid: string;
  name: string;
  level: number;
  bypassesPlayerLimit: boolean;
}

export interface BannedIpItem {
  ip: string;
  created: string;
  source: string;
  expires: string;
  reason: string;
}

export interface BannedPlayerItem {
  uuid: string;
  name: string;
  created: string;
  source: string;
  expires: string;
  reason: string;
}

export interface UserCacheItem {
  name: string;
  uuid: string;
  expiresOn: string;
}
