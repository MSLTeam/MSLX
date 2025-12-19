import { request } from '@/utils/request';
import { SettingsModel, WebpanelSettingsModel } from '@/api/model/settings';

export function getSettings() {
  return request.get<SettingsModel>({
    url: '/api/settings',
  });
}

export function updateSettings(data: SettingsModel) {
  return request.post({
    url: '/api/settings',
    data,
  });
}

export function updateWebpanelStyleSettings(data: WebpanelSettingsModel){
  return request.post({
    url: '/api/settings/webpanel/style',
    data,
  })
}

export function getWebpanelStyleSettings(){
  return request.get<WebpanelSettingsModel>({
    url: '/api/settings/webpanel/style',
  })
}
