import { request } from '@/utils/request';
import { SettingsModel } from '@/api/model/settings';

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
