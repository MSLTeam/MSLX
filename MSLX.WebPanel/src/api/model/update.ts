export interface UpdateInfoModel {
  needUpdate: boolean;
  currentVersion: string;
  latestVersion: string;
  status: string;
  environment: string;
  log: string;
}

export interface UpdateDownloadInfoModel {
  web: string;
  file: string;
}
