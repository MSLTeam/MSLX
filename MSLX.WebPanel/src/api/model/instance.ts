export interface CreateInstanceQucikModeModel {
  name: string;
  path: string;
  java: string;
  core: string;
  packageFileKey: string;
  coreFileKey: string;
  coreUrl: string;
  coreSha256: string;
  minM: number;
  maxM: number;
  args: string;
  ignoreEula?: boolean;
}

export interface InstanceListModel {
  id: number;
  name: string;
  basePath: string;
  java: string;
  core: string;
  icon: string;
  status: number;
}

export interface InstanceInfoModel {
  id: number;
  name: string;
  basePath: string;
  java: string;
  core: string;
  minM: number;
  maxM: number;
  status: number;
  uptime: string;
  mcConfig: {
    difficulty: string;
    levelName: string;
    gamemode: string;
    serverPort: string;
    onlineMode: string;
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
  autoRestart?: boolean;
  forceAutoRestart?: boolean;
  allowOriginASCIIColors?: boolean;
  ignoreEula?: boolean;
  forceJvmUTF8?: boolean;
  runOnStartup?: boolean;
  inputEncoding?: 'utf-8' | 'gbk';
  outputEncoding?: 'utf-8' | 'gbk';
  fileEncoding?: 'utf-8' | 'gbk';

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
