export interface SettingsModel {
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
}
