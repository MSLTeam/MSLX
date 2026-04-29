import { MessagePlugin } from 'tdesign-vue-next';
import NProgress from 'nprogress';
import 'nprogress/nprogress.css';

import { getPermissionStore, getUserStore } from '@/store';
import router from '@/router';
import { arePluginsLoaded, loadAllPlugins } from '@/utils/pluginManager';

NProgress.configure({ showSpinner: false });

// 404路由在这里兜底
const addCatchAllRoute = () => {
  if (!router.hasRoute('404Page')) {
    router.addRoute({
      path: '/:w+',
      name: '404Page',
      redirect: '/404',
    });
  }
};

router.beforeEach(async (to, from, next) => {
  window.document.title = to.meta.title ? `${to.meta.title} | MSLX 控制台` : 'MSLX 控制台';
  NProgress.start();

  const userStore = getUserStore();
  const permissionStore = getPermissionStore();
  const { whiteListRouters } = permissionStore;
  const { token } = userStore;

  if (token) {
    if (to.path === '/login' || to.path === '/oauth/callback') {
      next();
      return;
    }

    const { roles } = userStore;

    if (roles && roles.length > 0) {
      if (!arePluginsLoaded()) {
        if (!permissionStore.routers || permissionStore.routers.length === 0) {
          await permissionStore.initRoutes(roles);
        }

        // 挂载插件
        await loadAllPlugins();

        // 补上404路由
        addCatchAllRoute();

        next({ ...to, replace: true });
      } else {
        next();
      }
    } else {
      try {
        await userStore.getUserInfo();
        const { roles } = userStore;
        await permissionStore.initRoutes(roles);

        await loadAllPlugins();

        addCatchAllRoute();

        if (to.name && router.hasRoute(to.name)) {
          next();
        } else {
          next({ ...to, replace: true });
        }
      } catch (error) {
        MessagePlugin.error(error as string);
        next({
          path: '/login',
          query: { redirect: encodeURIComponent(to.fullPath) },
        });
        NProgress.done();
      }
    }
  } else {
    if (whiteListRouters.indexOf(to.path) !== -1) {
      next();
    } else {
      next({
        path: '/login',
        query: { redirect: encodeURIComponent(to.fullPath) },
      });
    }
    NProgress.done();
  }
});

router.afterEach((to) => {
  if (to.path === '/login') {
    const userStore = getUserStore();
    userStore.logout();
  }
  NProgress.done();
});
