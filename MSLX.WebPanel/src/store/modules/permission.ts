import { defineStore } from 'pinia';
import { RouteRecordRaw } from 'vue-router';
import router, { asyncRouterList } from '@/router';
import { store } from '@/store';

function filterPermissionsRouters(routes: Array<RouteRecordRaw>, roles: Array<any>) {
  const res = [];
  const removeRoutes = [];

  routes.forEach((route) => {
    const tmpRoute = { ...route };

    const hasPermission = (metaRole: any) => {
      if (!metaRole) return false;

      if (Array.isArray(metaRole)) {
        return roles.some(role => metaRole.includes(role));
      }

      return roles.includes(metaRole);
    };

    // 先判断父级路由是否有权访问
    if (hasPermission(tmpRoute.meta?.roleCode || tmpRoute.name)) {

      // 递归过滤子路由
      if (tmpRoute.children && tmpRoute.children.length > 0) {
        const childrenFilter = filterPermissionsRouters(tmpRoute.children, roles);
        tmpRoute.children = childrenFilter.accessedRouters;
      }

      res.push(tmpRoute);
    } else {
      removeRoutes.push(tmpRoute);
    }
  });

  return { accessedRouters: res, removeRoutes };
}

export const usePermissionStore = defineStore('permission', {
  state: () => ({
    whiteListRouters: ['/login','/oauth/callback'],
    routers: [] as RouteRecordRaw[], // 有权限的路由
    removeRoutes: [] as RouteRecordRaw[], // 没权限的路由
    dynamicRoutesAdded: [] as string[],
  }),
  actions: {
    async initRoutes(roles: Array<unknown>) {
      // 动态路由初始化：清除所有旧的动态路由，防止重复
      this.clearRoutes();

      let accessedRouters: RouteRecordRaw[] = [];
      let removeRoutes: RouteRecordRaw[] = [];

      // 根据角色计算出你有权限的路由
      if (roles.includes('all')) {
        accessedRouters = asyncRouterList;
      } else {
        const res = filterPermissionsRouters(asyncRouterList, roles);
        accessedRouters = res.accessedRouters;
        removeRoutes = res.removeRoutes;
      }

      // 遍历有权限的路由，使用 router.addRoute() 添加
      const addedRouteNames: string[] = [];
      accessedRouters.forEach((route) => {
        router.addRoute(route);
        // 记录添加的路由 name，以便后续移除
        if (route.name) {
          addedRouteNames.push(route.name as string);
        }
      });

      // 更新 store state
      this.routers = accessedRouters;
      this.removeRoutes = removeRoutes;
      this.dynamicRoutesAdded = addedRouteNames;
    },

    /**
     * +++ 新增 clearRoutes 动作 +++
     * 此动作用于清除所有由 initRoutes 添加的动态路由
     */
    async clearRoutes() {
      const routesToRemove = this.dynamicRoutesAdded;

      routesToRemove.forEach((routeName) => {
        if (router.hasRoute(routeName)) {
          router.removeRoute(routeName);
        }
      });

      // 重置 state
      this.routers = [];
      this.removeRoutes = [];
      this.dynamicRoutesAdded = [];
    },

  },
});

export function getPermissionStore() {
  return usePermissionStore(store);
}
