import { request } from '@/utils/request';

export function linkSlaveNode(data: { nodeUrl: string, nodeName: string, linkKey: string, pairingId: string }) {
  return request.post({
    url: '/api/node/link',
    data
  });
}

export function getSlaveNodes() {
  return request.get({
    url: '/api/node/list'
  });
}

export function getPublicSlaveNode(nodeId: string) {
  return request.get<any>({
    url: '/api/node/public-target',
    params: { nodeId }
  }, { requestToSlaveNode: false, withToken: false });
}

export function unlinkSlaveNode(id: string, force = false) {
  return request.post({
    url: `/api/node/delete/${id}${force ? '?force=true' : ''}`
  });
}

export function postEditSlaveNode(id: string, data: { nodeUrl: string, nodeName: string, linkKey: string, nodeTags?: string }) {
  return request.post({
    url: `/api/node/update/${id}`,
    data
  });
}

export interface NodeSyncSelection {
  targetNodeIds: string[];
  settingKeys: string[];
  pluginIds: string[];
  includePluginConfig: boolean;
  sourceRevision?: string;
  targetRevisions?: Record<string, string>;
}
export function previewNodeSync(data: NodeSyncSelection) {
  return request.post<any>({ url: '/api/node/sync/preview', data, timeout: 120000 }, { targetNodeId: 'local', requestToSlaveNode: false });
}
export function startNodeSync(data: NodeSyncSelection) {
  return request.post<{ taskId: string }>({ url: '/api/node/sync', data, timeout: 120000 }, { targetNodeId: 'local', requestToSlaveNode: false });
}
