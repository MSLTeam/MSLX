import { MessagePlugin } from 'tdesign-vue-next';
import NProgress from 'nprogress';
import 'nprogress/nprogress.css';
import { getPermissionStore, getUserStore, useNodeStore } from '@/store';
import router from '@/router';
import Layout from '@/layouts/index.vue';
import { getNodeTarget } from '@/utils/nodeTarget';
import { arePluginsLoaded, loadAllPlugins, activatePluginNode, setPluginLoadError } from '@/utils/pluginManager';
import { usePluginUIStore } from '@/store/modules/pluginUI';

NProgress.configure({ showSpinner: false });
const addCatchAllRoute = () => {
  if (!router.hasRoute('404Page')) router.addRoute({
    path: '/:pathMatch(.*)*', name: '404Page', component: Layout,
    children: [{ path: '', component: () => import('@/pages/notFound/index.vue') }],
  });
};

router.beforeEach(async (to, _from, next) => {
  document.title = to.meta.title ? `${to.meta.title} | MSLX 控制台` : 'MSLX 控制台';
  NProgress.start();
  const user = getUserStore();
  const permission = getPermissionStore();
  if (to.path === '/login' || to.path === '/oauth/callback') { next(); return; }
  if (user.token) {
    try {
      if (!user.roles?.length) await user.getUserInfo();
      if (!permission.routers?.length) await permission.initRoutes(user.roles);
    } catch (error) {
      MessagePlugin.error(error instanceof Error ? error.message : '登录状态已失效');
      next({ path: '/login', query: { redirect: encodeURIComponent(to.fullPath) } });
      NProgress.done(); return;
    }
  }
  const nodeId = typeof to.query.node === 'string' && to.query.node ? to.query.node : 'local';
  if (nodeId !== 'local' && !user.token) {
    next({ path: '/login', query: { redirect: encodeURIComponent(to.fullPath) } }); NProgress.done(); return;
  }
  try {
    if (nodeId !== 'local') { await useNodeStore().fetchNodes(); getNodeTarget(nodeId); }
    if (!arePluginsLoaded(nodeId)) await loadAllPlugins(nodeId);
  } catch (error) {
    setPluginLoadError(nodeId, error);
    const ui = usePluginUIStore();
    ui.error = error instanceof Error ? error.message : '目标节点插件加载失败';
    ui.loading = false;
  }
  addCatchAllRoute();
  const resolved = router.resolve(to.fullPath);
  const anonymous = permission.whiteListRouters.includes(to.path) || resolved.matched.some(record => record.meta?.anonymous);
  if (!user.token && !anonymous) {
    next({ path: '/login', query: { redirect: encodeURIComponent(to.fullPath) } }); NProgress.done(); return;
  }
  if (resolved.matched.length !== to.matched.length || resolved.matched.some((record, index) => record !== to.matched[index])) {
    next({ path: to.path, query: to.query, hash: to.hash, replace: true }); return;
  }
  next();
});

router.afterEach(to => {
  const nodeId = typeof to.query.node === 'string' && to.query.node ? to.query.node : 'local';
  useNodeStore().activeNodeId = nodeId;
  activatePluginNode(nodeId);
  if (to.path === '/login') getUserStore().logout();
  NProgress.done();
});
