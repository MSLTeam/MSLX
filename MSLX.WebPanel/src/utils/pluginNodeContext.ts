import { defineComponent, h, inject, onBeforeUnmount, provide, shallowRef } from 'vue';
import * as signalR from '@microsoft/signalr';
import { request } from '@/utils/request';
import { BASE_URL_NAME, TOKEN_NAME } from '@/config/global';
import { getPageNodeId, getNodeTarget, nodeResourceUrl } from './nodeTarget';
import { getMediaToken, getDownloadToken } from './scopedToken';
import { getHubUrl, getHubOptions } from './hub';

const contextKey = Symbol.for('MSLX.PluginNodeContext');
const account = () => `${localStorage.getItem(BASE_URL_NAME)}:${localStorage.getItem(TOKEN_NAME)}`;

export function createPluginNodeContext(nodeId = getPageNodeId(), pluginId = '') {
  const owner = account();
  const abort = new AbortController();
  const hubs = new Set<signalR.HubConnection>();
  let disposed = false;
  const assertActive = () => {
    if (disposed || owner !== account()) throw new Error('插件页面或登录状态已变化，请重新操作');
    getNodeTarget(nodeId);
  };
  const send = (config: any, options: any = {}) => {
    assertActive();
    if (/^(?:[a-z][a-z0-9+.-]*:|\/\/)/i.test(config.url || '')) throw new Error('节点上下文请求请使用相对 API 地址');
    return request.request({ ...config, signal: config.signal || abort.signal }, {
      ...options,
      targetNodeId: options.targetNodeId || config.headers?.['x-node-id'] || (options.requestToSlaveNode === false ? 'local' : nodeId),
    });
  };
  const client = {
    request: send,
    get: (config: any, options?: any) => send({ ...config, method: 'GET' }, options),
    post: (config: any, options?: any) => send({ ...config, method: 'POST' }, options),
    put: (config: any, options?: any) => send({ ...config, method: 'PUT' }, options),
    patch: (config: any, options?: any) => send({ ...config, method: 'PATCH' }, options),
    delete: (config: any, options?: any) => send({ ...config, method: 'DELETE' }, options),
  };
  return Object.freeze({
    nodeId, pluginId, request: client,
    getTarget: () => { assertActive(); return getNodeTarget(nodeId); },
    async resourceUrl(path: string, scope: 'media' | 'download' = 'media') {
      assertActive();
      const token = await (scope === 'media' ? getMediaToken(nodeId) : getDownloadToken(nodeId));
      assertActive();
      return nodeResourceUrl(path, token, scope === 'media' ? 'media_token' : 'download_token').toString();
    },
    createHub(path: string) {
      assertActive();
      const url = getHubUrl(path, nodeId);
      const options = getHubOptions(url);
      const factory = options.accessTokenFactory;
      const hub = new signalR.HubConnectionBuilder().withUrl(url, {
        ...options, ...(factory ? { accessTokenFactory: () => { assertActive(); return factory(); } } : {}),
      }).withAutomaticReconnect().build();
      hubs.add(hub);
      return hub;
    },
    dispose() {
      disposed = true; abort.abort();
      for (const hub of hubs) void hub.stop();
      hubs.clear();
    },
  });
}

export function usePluginNodeContext() {
  const supplied = inject<ReturnType<typeof createPluginNodeContext> | null>(contextKey, null);
  if (!supplied) throw new Error('请在宿主插件组件内使用节点上下文');
  return supplied;
}

// Keep attrs, slots, events and exposed methods intact for all nine extension slots.
export function bindPluginComponent(component: any, nodeId: string, pluginId: string) {
  return defineComponent({
    name: 'NodeBoundPlugin', inheritAttrs: false,
    setup(_props, { attrs, slots, expose }) {
      const child = shallowRef<any>();
      const context = createPluginNodeContext(nodeId, pluginId);
      provide(contextKey, context);
      onBeforeUnmount(() => context.dispose());
      expose(new Proxy({}, {
        has: (target, key) => Reflect.has(target, key) || !!child.value && key in child.value,
        get: (target, key) => {
          if (Reflect.has(target, key)) return Reflect.get(target, key);
          const value = child.value?.[key];
          return typeof value === 'function' ? value.bind(child.value) : value;
        },
      }));
      return () => h(component, { ...attrs, ref: child }, slots);
    },
  });
}
