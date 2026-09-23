export interface SettingsModel {
  allowNormalUserChangeUserName: boolean;
  allowNormalUserEditFrpConfig: boolean;
  allowNormalUserOfflineDownload: boolean;
  enableSsrfProtection: boolean;
  fireWallBanLocalAddr: boolean;
  openWebConsoleOnLaunch: boolean;
  neoForgeInstallerMirrors: string;
  listenHost: string;
  listenPort: number;
  allowExternalAccess: boolean;
  isEmbeddedDaemon: boolean;
  oAuthMSLClientID: string;
  oAuthMSLClientSecret: string;
  downloadThreadCount: number;
  enableCdnProxy?: boolean;
  cdnProxyIpHeader?: string;
  cdnProxySecretValue?: string;
}

export interface WebpanelSettingsModel {
  webPanelStyleDarkBackgroundOpacity: number;
  webPanelStyleDarkComponentsOpacity: number;
  webPanelStyleLightBackground: string;
  webPanelStyleLightBackgroundOpacity: number;
  webPanelStyleLightComponentsOpacity: number;
  webPanelStyleDarkBackground: string;
  webpPanelTerminalBlurLight: number;
  webpPanelTerminalBlurDark: number;
  webPanelColorizeLogLevel: number;
}

export interface SslSettingsResponse {
  enableSsl: boolean;
  hasCertificate: boolean;
  certificateContent?: string | null;
  isSelfSigned: boolean;
}

export interface UpdateSslSettingsRequest {
  enableSsl: boolean;
  useSelfSignedCert: boolean;
  certificate?: string;
  privateKey?: string;
}
