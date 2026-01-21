export interface FrpListModel {
  id: number;
  name: string;
  service: string;
  configType: string;
  status: boolean;
}

export interface ProxyInfoModel {
  proxyName: string;
  type: string;
  localAddress: string;
  remoteAddressMain: string;
  remoteAddressBackup: string;
}

export interface TunnelInfoModel{
  isRunning: boolean;
  proxies: ProxyInfoModel[];
}

