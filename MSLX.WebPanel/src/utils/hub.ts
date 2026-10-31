import { useUserStore } from '@/store';
import { getPageNodeId, getNodeTarget, getNodeToken } from './nodeTarget';

export function getHubUrl(hubPath: string, targetNodeId?: string): string {
  const userStore = useUserStore();
  const { baseUrl, token } = userStore;

  const activeNodeId = targetNodeId ?? getPageNodeId();

  // 白名单 这些允许请求到子节点
  const slaveHubs = [
    '/api/hubs/frpLogsHub',
    '/api/hubs/updateProgressHub',
    '/api/hubs/creationProgressHub',
    '/api/hubs/instanceControlHub',
    '/api/hubs/aiChatHub',
  ];

  const isSlaveRoute = targetNodeId !== undefined || slaveHubs.includes(hubPath) || slaveHubs.some(h => hubPath.startsWith(h));

  let finalBase = baseUrl || window.location.origin;

  if (activeNodeId !== 'local' && isSlaveRoute) {
    finalBase = getNodeTarget(activeNodeId).baseUrl;
  }

  const hubUrl = new URL(hubPath, finalBase);
  if (token && (activeNodeId === 'local' || !isSlaveRoute)) {
    hubUrl.searchParams.append('x-user-token', token);
  }
  if (activeNodeId && activeNodeId !== 'local' && isSlaveRoute) {
    hubUrl.searchParams.append('x-node-id', activeNodeId);
  }

  return hubUrl.toString();
}

// SignalR 重连的时候调用这个（存在续订调用）
export function getHubOptions(hubUrl: string) {
  const nodeId = new URL(hubUrl).searchParams.get('x-node-id');
  return nodeId ? { withCredentials: false, accessTokenFactory: () => getNodeToken(nodeId) } : { withCredentials: false };
}
