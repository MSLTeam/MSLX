import router from '@/router';
import Layout from '@/layouts/index.vue';
import { getPermissionStore, usePluginUIStore, useUserStore } from '@/store';
import { getPluginList } from '@/api/plugins';
import { PluginMultiNodeSupport } from '@/api/model/plugins';
import { markRaw, ref } from 'vue';
import { BASE_URL_NAME, TOKEN_NAME } from '@/config/global';
import { getPageNodeId, getNodeTarget, pluginNodePages } from './nodeTarget';
import { bindPluginComponent } from './pluginNodeContext';
import { request } from './request';

export const pluginStateChanged = ref(false);
const cache = new Map<string, Array<{ id: string; config: any }>>();
const pending = new Map<string, Promise<void>>();
const errors = new Map<string, string>();
let owner = '';
let activeNode: string | null = null;
let generation = 0;
let removers: Array<() => void> = [];
const anonymousPaths = new Set<string>();
const account = () => `${localStorage.getItem(BASE_URL_NAME)}:${localStorage.getItem(TOKEN_NAME)}`;

function clearRegistrations() {
  removers.forEach(remove => remove()); removers = [];
  const permission = getPermissionStore();
  const strip = (menus: any[]): any[] => menus.filter(menu => !menu.meta?.pluginOwner).map(menu => ({ ...menu,
    ...(menu.children ? { children: strip(menu.children) } : {}),
  }));
  permission.routers = strip(permission.routers || []);
  permission.whiteListRouters = permission.whiteListRouters.filter(path => !anonymousPaths.has(path));
  anonymousPaths.clear(); activeNode = null;
}

export function resetPluginRuntime() {
  generation++; clearRegistrations(); pluginNodePages.clear(); cache.clear(); errors.clear(); pending.clear();
  usePluginUIStore().clear(); owner = account();
}

function checkOwner() { if (owner !== account()) resetPluginRuntime(); }
export function arePluginsLoaded(nodeId = getPageNodeId()) {
  checkOwner();
  return activeNode === nodeId && cache.has(nodeId);
}

function findMenu(menus: any[], name: string): any {
  for (const menu of menus) {
    if (menu.name === name) return menu;
    const found = menu.children && findMenu(menu.children, name);
    if (found) return found;
  }
}

function install(configs: Array<{ id: string; config: any }>, nodeId: string) {
  clearRegistrations();
  const permission = getPermissionStore();
  const ui = usePluginUIStore();
  ui.extensionsByNode[nodeId] = {};
  const role = useUserStore().isAdmin ? 'admin' : 'user';
  const allowed = (route: any) => !route.meta?.roleCode || route.meta.roleCode.includes(role);
  for (const { id, config } of configs) {
    const wrapRoute = (raw: any): any => {
      let component = raw.component;
      if (component === 'HOST_LAYOUT') component = Layout;
      else if (component === 'HOST_BLANK_LAYOUT') component = () => import('@/layouts/blank.vue');
      else if (component === 'PLUGINS_PUBLIC_LAYOUT') component = () => import('@/layouts/plugins-public.vue');
      else if (typeof component === 'function') {
        const lazy = component;
        component = async () => { const module = await lazy(); return bindPluginComponent(module.default || module, nodeId, id); };
      } else if (component) component = markRaw(bindPluginComponent(component, nodeId, id));
      return { ...raw, component,
        meta: { ...raw.meta, pluginOwner: id, nodeScoped: true, ...(nodeId !== 'local' && !raw.meta?.anonymous ? { anonymous: false } : {}) },
        ...(raw.children ? { children: raw.children.filter(allowed).map(wrapRoute) } : {}),
      };
    };
    for (const raw of config.routes || []) {
      if (!allowed(raw)) continue;
      const route = wrapRoute(raw);
      const parent = raw.parentName ? findMenu(permission.routers || [], raw.parentName) : null;
      if (raw.parentName && !parent) { console.warn(`插件 ${id} 的父菜单 ${raw.parentName} 不可用，已跳过该路由`); continue; }
      delete route.parentName;
      if (parent && !route.path.startsWith('/')) route.path = `${parent.path}/${route.path}`;
      const ensureUnique = (item: any) => {
        if (item.name && router.hasRoute(item.name)) throw new Error(`插件路由名称冲突: ${item.name}`);
        (item.children || []).forEach(ensureUnique);
      };
      ensureUnique(route);
      removers.push(parent ? router.addRoute(raw.parentName, route) : router.addRoute(route));
      if (parent) parent.children = [...(parent.children || []), route];
      else permission.routers = [...(permission.routers || []), route];
      const collect = (item: any, base = '') => {
        const full = item.path.startsWith('/') ? item.path : `${base}/${item.path}`.replace(/\/$/, '');
        const root = full.split('/:')[0];
        if (root) pluginNodePages.add(root);
        if (item.meta?.anonymous && !permission.whiteListRouters.includes(full)) {
          permission.whiteListRouters.push(full); anonymousPaths.add(full);
        }
        (item.children || []).forEach(child => collect(child, full));
      };
      collect(route);
    }
    for (const ext of config.extensions || []) {
      if (ext.slot && ext.component) ui.registerExtension(ext.slot, {
        ...ext, pluginId: id, nodeId,
        component: markRaw(bindPluginComponent(ext.component, nodeId, id)),
        ...(ext.icon && typeof ext.icon === 'object' ? { icon: markRaw(ext.icon) } : {}),
      }, nodeId);
    }
  }
  permission.routers = [...permission.routers];
  activeNode = nodeId;
}

export function invalidateNodePlugins(nodeId: string) {
  cache.delete(nodeId); errors.delete(nodeId);
}

export function setPluginLoadError(nodeId: string, error: unknown) {
  errors.set(nodeId, error instanceof Error ? error.message : '目标节点插件加载失败');
}

export function activatePluginNode(nodeId: string) {
  const ui = usePluginUIStore();
  ui.nodeId = nodeId; ui.loading = false; ui.error = errors.get(nodeId) || '';
}

export async function loadAllPlugins(nodeId = getPageNodeId(), force = false): Promise<void> {
  checkOwner();
  if (!force && arePluginsLoaded(nodeId)) return;
  if (!force && pending.has(nodeId)) return pending.get(nodeId);
  if (!force && errors.has(nodeId)) throw new Error(errors.get(nodeId));
  const currentOwner = owner;
  const attempt = ++generation;
  const ui = usePluginUIStore();
  ui.loading = true; ui.error = '';
  if (force) cache.delete(nodeId);
  const promise = (async () => {
    try {
      clearRegistrations();
      if (!cache.has(nodeId)) {
        if (nodeId !== 'local') {
          const protocol = await request.get<any>({ url: '/api/node/protocol' }, { targetNodeId: nodeId });
          if (!protocol?.features?.pluginMultiNode) throw new Error('子节点不支持插件多节点适配，请一起升级主、子节点');
        }
        const plugins = await getPluginList(undefined, nodeId);
        const configs = await Promise.all(plugins.filter(plugin => plugin.status === '已启用' && plugin.entryPath &&
          (nodeId === 'local' || plugin.supportsMultiNode !== PluginMultiNodeSupport.Unsupported)).map(async plugin => {
          const url = new URL(plugin.entryPath, getNodeTarget(nodeId).baseUrl);
          url.searchParams.set('revision', plugin.frontendRevision || plugin.version);
          const module = await import(/* @vite-ignore */ url.toString());
          if (!module.pluginConfig) throw new Error(`插件 ${plugin.name} 未导出 pluginConfig`);
          return { id: plugin.id, config: module.pluginConfig };
        }));
        if (currentOwner !== account() || attempt !== generation) throw new Error('插件目标或登录状态已变化');
        cache.set(nodeId, configs);
      }
      if (currentOwner !== account() || attempt !== generation) throw new Error('插件目标或登录状态已变化');
      install(cache.get(nodeId)!, nodeId);
      errors.delete(nodeId);
    } catch (error) {
      if (attempt === generation && currentOwner === account()) {
        clearRegistrations();
        cache.delete(nodeId);
        errors.set(nodeId, error instanceof Error ? error.message : '插件加载失败');
      }
      throw error;
    }
  })().finally(() => { if (pending.get(nodeId) === promise) pending.delete(nodeId); });
  pending.set(nodeId, promise);
  return promise;
}

// Development entry remains available without changing the normal node loader.
export async function loadPlugin(pluginUrl: string) {
  const nodeId = getPageNodeId();
  const module = await import(/* @vite-ignore */ pluginUrl);
  const configs = [...(cache.get(nodeId) || []), { id: module.pluginConfig.name, config: module.pluginConfig }];
  cache.set(nodeId, configs); install(configs, nodeId); activatePluginNode(nodeId);
}
