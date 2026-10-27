import { request } from '@/utils/request';
import { getPageNodeId } from '@/utils/nodeTarget';
import {
  SettingsModel,
  SslSettingsResponse,
  UpdateSslSettingsRequest,
  WebpanelSettingsModel,
} from '@/api/model/settings';

export function getSettings(nodeId = getPageNodeId()) {
  return request.get<SettingsModel>({
    url: '/api/settings',
  }, { targetNodeId: nodeId });
}

export function updateSettings(data: SettingsModel, nodeId = getPageNodeId()) {
  return request.post({
    url: '/api/settings',
    data,
  }, { targetNodeId: nodeId });
}

export function updateWebpanelStyleSettings(data: WebpanelSettingsModel){
  return request.post({
    url: '/api/settings/webpanel/style',
    data,
  }, { requestToSlaveNode: false, targetNodeId: 'local' })
}

export function getWebpanelStyleSettings(){
  return request.get<WebpanelSettingsModel>({
    url: '/api/settings/webpanel/style',
  }, { requestToSlaveNode: false, targetNodeId: 'local' })
}

export function getSslSettings(nodeId = getPageNodeId()) {
  return request.get<SslSettingsResponse>({
    url: '/api/settings/ssl',
  }, { targetNodeId: nodeId });
}

export function updateSslSettings(data: UpdateSslSettingsRequest, nodeId = getPageNodeId()) {
  return request.post({
    url: '/api/settings/ssl',
    data,
  }, { targetNodeId: nodeId });
}
