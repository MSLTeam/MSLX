import { useUserStore } from '@/store';

export function getHubUrl(hubPath: string): string {
  const userStore = useUserStore();
  const { baseUrl, token } = userStore;

  const activeNodeId = localStorage.getItem('ACTIVE_NODE_ID');
  const activeNodeUrl = localStorage.getItem('ACTIVE_NODE_URL');

  // 白名单 这些允许请求到子节点
  const slaveHubs = [
    '/api/hubs/frpLogsHub',
    '/api/hubs/updateProgressHub',
    '/api/hubs/creationProgressHub',
    '/api/hubs/instanceControlHub',
  ];

  const isSlaveRoute = slaveHubs.includes(hubPath) || slaveHubs.some(h => hubPath.startsWith(h));

  let finalBase = baseUrl || window.location.origin;

  if (activeNodeUrl && activeNodeId !== 'local' && isSlaveRoute) {
    finalBase = activeNodeUrl;
  }

  // 拼接 URL 节点ID Token
  const hubUrl = new URL(hubPath, finalBase);
  if (token) {
    hubUrl.searchParams.append('x-user-token', token);
  }
  if (activeNodeId && activeNodeId !== 'local' && isSlaveRoute) {
    hubUrl.searchParams.append('x-node-id', activeNodeId);
  }

  return hubUrl.toString();
}
