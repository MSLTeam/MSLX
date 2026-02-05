import Layout from '@/layouts/index.vue';

export default [
  {
    path: '/dashboard',
    redirect: '/dashboard/base',
    component: Layout,
    name: 'dashboard',
    meta: {
      title: '仪表盘',
      icon: 'dashboard',
    },
    children: [
      {
        path: 'base',
        name: 'DashboardBaseIndex',
        component: () => import('@/pages/dashboard/base/index.vue'),
        meta: { title: '仪表盘', hidden: true },
      },
    ],
  },
  {
    path: '/instance',
    component: Layout,
    // redirect: '/instance/list',
    name: 'instance',
    meta: { title: '服务端管理', icon: 'server' },
    children: [
      {
        path: 'list',
        name: 'InstanceList',
        component: () => import('@/pages/instance/instanceList/index.vue'),
        meta: { title: '服务端列表', icon: 'grid-view' },
      },
      {
        path: 'create',
        name: 'InstanceCreate',
        component: () => import('@/pages/instance/createInstance/index.vue'),
        meta: { title: '创建服务端', icon: 'add' },
      },
      {
        path: 'backup',
        name: 'InstanceBackupManager',
        component: () => import('@/pages/instance/backupManager/index.vue'),
        meta: { title: '备份管理', icon: 'backup' },
      },
    ],
  },
  {
    path: '/frp',
    component: Layout,
    name: 'frp',
    meta: { title: '隧道管理', icon: 'rocket' },
    children: [
      {
        path: 'list',
        name: 'FrpList',
        component: () => import('@/pages/frp/list/index.vue'),
        meta: { title: '隧道列表', icon: 'format-vertical-align-left' },
      },
      {
        path: 'create',
        name: 'FrpCreate',
        component: () => import('@/pages/frp/createFrp/index.vue'),
        meta: { title: '创建隧道', icon: 'add' },
      },
    ],
  },
];
