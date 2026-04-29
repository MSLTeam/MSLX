import router, { asyncRouterList } from '@/router';
import Layout from '@/layouts/index.vue';
import { getPermissionStore } from '@/store';

let pluginsLoaded = false;

export function arePluginsLoaded() {
  return pluginsLoaded;
}

export async function loadAllPlugins() {
  if (pluginsLoaded) return;

  // const pluginUrls = ['http://localhost:5001/mslx-plugin-entry.js'];
  // await Promise.all(pluginUrls.map((url) => loadPlugin(url)));

  pluginsLoaded = true;
  console.log('[MSLX Plugin] 🎉 所有插件加载初始化完毕！');
}

function findMenuByName(menus: any[], name: string): any {
  if (!menus) return null;
  for (const menu of menus) {
    if (menu.name === name) return menu;
    if (menu.children && menu.children.length > 0) {
      const found = findMenuByName(menu.children, name);
      if (found) return found;
    }
  }
  return null;
}

export async function loadPlugin(pluginUrl: string) {
  try {
    const url = process.env.NODE_ENV === 'development' ? `${pluginUrl}?t=${Date.now()}` : pluginUrl;
    const module = await import(/* @vite-ignore */ url);
    const plugin = module.pluginConfig;

    if (!plugin || !plugin.routes) return;

    const permissionStore = getPermissionStore();

    const reactiveRouters = permissionStore.routers || [];
    const searchSource = reactiveRouters.length > 0 ? reactiveRouters : asyncRouterList;

    let hasChanges = false;

    plugin.routes.forEach((rawRoute: any) => {
      const route = { ...rawRoute };

      if (route.component === 'HOST_LAYOUT') {
        route.component = Layout;
      }

      const parentName = route.parentName;
      delete route.parentName;

      if (parentName) {
        const parentMenu = findMenuByName(searchSource, parentName);

        if (parentMenu) {
          if (!route.path.startsWith('/')) {
            route.path = `${parentMenu.path}/${route.path}`;
          }

          router.addRoute(parentName, route);

          parentMenu.children = parentMenu.children ? [...parentMenu.children] : [];
          parentMenu.children.push(route);
          hasChanges = true;
        } else {
          console.error(`[MSLX Plugin] 找不到父菜单 [${parentName}]！`);
        }
      } else {
        router.addRoute(route);
        if (reactiveRouters) reactiveRouters.push(route);
        if (searchSource !== reactiveRouters) searchSource.push(route);
        hasChanges = true;
      }
    });

    if (hasChanges && permissionStore.routers) {
      permissionStore.routers = [...reactiveRouters];
    }

    console.log(`[MSLX Plugin]  插件 [${plugin.name}] 路由挂载成功!`);
  } catch (e) {
    console.error('[MSLX Plugin] 插件加载失败:', e);
  }
}
