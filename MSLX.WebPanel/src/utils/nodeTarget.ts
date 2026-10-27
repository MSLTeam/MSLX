import { request } from '@/utils/request';
import { useNodeStore } from '@/store/modules/node';
import { BASE_URL_NAME, TOKEN_NAME } from '@/config/global';
import { reactive } from 'vue';

export const getPageNodeId = () => new URL(window.location.href).searchParams.get('node') || 'local';
export const isNodePage = (path: string) => /^\/(?:instance|frp)(?:\/|$)/.test(path) || path === '/settings/docker';

// 面板生成的节点url才集成
export function nodeNavigationUrl(path: string, nodeId = getPageNodeId()) {
  if (!path.startsWith('/') || path.startsWith('//') || nodeId === 'local') return path;
  const url = new URL(path, window.location.origin);
  if (!isNodePage(url.pathname) || url.searchParams.has('node')) return path;
  url.searchParams.set('node', nodeId);
  return `${url.pathname}${url.search}${url.hash}`;
}

export function getNodeTarget(nodeId = getPageNodeId()) {
  if (nodeId === 'local') return { nodeId, baseUrl: localStorage.getItem(BASE_URL_NAME) || window.location.origin };
  const nodeStore = useNodeStore();
  const node = nodeStore.slaveNodes.find(n => n.nodeId === nodeId && !n.pendingRemoval);
  if (!node) throw new Error(nodeStore.fetchError || '节点不存在、已解除链接或无访问权限');
  return { nodeId, baseUrl: node.nodeUrl as string };
}

const tokens = reactive(new Map<string, { token: string; expiresAt: number }>());
const pending = new Map<string, Promise<string>>();
let owner = '';

export async function getNodeToken(nodeId: string, scope = ''): Promise<string> {
  const currentOwner = `${localStorage.getItem(BASE_URL_NAME)}:${localStorage.getItem(TOKEN_NAME)}`;
  if (currentOwner !== owner) { tokens.clear(); pending.clear(); owner = currentOwner; }
  const key = `${nodeId}:${scope}`;
  const existing = tokens.get(key);
  if (existing && existing.expiresAt * 1000 > Date.now() + 30000) return existing.token;
  const inFlight = pending.get(key);
  if (inFlight) return inFlight;
  const promise = request.post<{ token: string; expiresAt: number }>({
    url: '/api/node/session', data: { nodeId, scope },
  }, { requestToSlaveNode: false }).then(res => {
    if (owner !== currentOwner || currentOwner !== `${localStorage.getItem(BASE_URL_NAME)}:${localStorage.getItem(TOKEN_NAME)}`)
      throw new Error('登录状态已变化，请重新操作');
    tokens.set(key, res);
    return res.token;
  }).finally(() => { if (owner === currentOwner) pending.delete(key); });
  pending.set(key, promise);
  return promise;
}

export function getCachedNodeToken(nodeId: string, scope: string) {
  if (owner !== `${localStorage.getItem(BASE_URL_NAME)}:${localStorage.getItem(TOKEN_NAME)}`) return '';
  const cached = tokens.get(`${nodeId}:${scope}`);
  return cached && cached.expiresAt * 1000 > Date.now() ? cached.token : '';
}

// 异步下载换了节点就boom了～
export function nodeResourceUrl(path: string, token: string, parameter: 'media_token' | 'download_token') {
  if (!token) throw new Error('资源访问凭据尚未就绪，请重试');
  let payload: Record<string, unknown>;
  try {
    const parts = token.split('.');
    if (parts.length !== 3 || parts.some(part => !part) || !/^[A-Za-z0-9_-]+$/.test(parts[1])) throw new Error();
    const encoded = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')), c => c.charCodeAt(0));
    payload = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) throw new Error();
    if (typeof payload.UserId !== 'string' || !payload.UserId || payload.Scope !== parameter.replace('_token', '') ||
        typeof payload.exp !== 'number' || !Number.isFinite(payload.exp) || payload.exp * 1000 <= Date.now()) throw new Error();
    if ('NodeId' in payload && (typeof payload.NodeId !== 'string' || !payload.NodeId || payload.NodeId === 'local')) throw new Error();
  } catch {
    throw new Error('资源访问凭据格式无效，请重新获取');
  }
  const nodeId = (payload.NodeId as string | undefined) ?? 'local';
  const url = new URL(path, getNodeTarget(nodeId).baseUrl);
  url.searchParams.set(parameter, token);
  if (nodeId !== 'local') url.searchParams.set('x-node-id', nodeId);
  return url;
}
