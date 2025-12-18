export interface SettingsModel {
  fireWallBanLocalAddr: boolean;
  openWebConsoleOnLaunch: boolean;
  neoForgeInstallerMirrors: string;
  listenHost: string;
  listenPort: number;
}

export interface WebpanelSettingsModel {
  webPanelStyleDarkBackgroundOpacity: number;
  webPanelStyleDarkComponentsOpacity: number;
  webPanelStyleLightBackground: string;
  webPanelStyleLightBackgroundOpacity: number;
  webPanelStyleLightComponentsOpacity: number;
  webPanelStyleNightBackground: string;
}
