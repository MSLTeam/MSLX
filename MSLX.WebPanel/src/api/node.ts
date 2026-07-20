import { request } from '@/utils/request';

export function linkSlaveNode(data: { nodeUrl: string, nodeName: string, linkKey: string }) {
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

export function unlinkSlaveNode(id: string) {
  return request.post({
    url: `/api/node/delete/${id}`
  });
}

export function postEditSlaveNode(id: string, data: { nodeUrl: string, nodeName: string, linkKey: string, nodeTags?: string }) {
  return request.post({
    url: `/api/node/update/${id}`,
    data
  });
}
