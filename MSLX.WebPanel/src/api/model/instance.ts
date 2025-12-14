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
}

export interface InstanceListModel{
  id:number;
  name:string;
  basePath:string;
  java:string;
  core:string;
  icon:string;
  status:boolean;
}

export interface InstanceInfoModel{
  id:number;
  name:string;
  basePath:string;
  java:string;
  core:string;
  minM: number;
  maxM: number;
  status:boolean;
  uptime:string;
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
  yggdrasilApiAddr?: string;
  backupMaxCount?: number;
  backupDelay?: number;
  backupPath?: string;
  autoRestart?: boolean;
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
  yggdrasilApiAddr?: string;
  backupMaxCount?: number;
  backupDelay?: number;
  backupPath?: string;
  autoRestart?: boolean;
  runOnStartup?: boolean;
  inputEncoding?: 'utf-8' | 'gbk';
  outputEncoding?: 'utf-8' | 'gbk';
}

export interface UpdateInstanceResponseModel {
  needListen: boolean;
}
