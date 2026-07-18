export interface SettingsModel {
  allowNormalUserChangeUserName: boolean;
  fireWallBanLocalAddr: boolean;
  openWebConsoleOnLaunch: boolean;
  neoForgeInstallerMirrors: string;
  listenHost: string;
  listenPort: number;
  oAuthMSLClientID: string;
  oAuthMSLClientSecret: string;
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
