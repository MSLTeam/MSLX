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
      roleCode: ['admin', 'user']
    },
    children: [
      {
        path: 'base',
        name: 'DashboardBaseIndex',
        component: () => import('@/pages/dashboard/base/index.vue'),
        meta: { title: '仪表盘', hidden: true,roleCode: ['admin', 'user'] },
      },
    ],
  },
  {
    path: '/instance',
    component: Layout,
    // redirect: '/instance/list',
    name: 'instance',
    meta: { title: '服务端管理', icon: 'server',roleCode: ['admin', 'user'] },
    children: [
      {
        path: 'list',
        name: 'InstanceList',
        component: () => import('@/pages/instance/instanceList/index.vue'),
        meta: { title: '服务端列表', icon: 'grid-view',roleCode: ['admin', 'user'] },
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
        meta: { title: '备份管理', icon: 'backup',roleCode: ['admin', 'user'] },
      },
      {
        path: 'cron',
        name: 'InstanceCronTasksManager',
        component: () => import('@/pages/instance/cronTasksManager/index.vue'),
        meta: { title: '定时任务', icon: 'time',roleCode: ['admin', 'user'] },
      },
    ],
  },
  {
    path: '/frp',
    component: Layout,
    name: 'frp',
    meta: { title: '隧道管理', icon: 'rocket',roleCode: ['admin', 'user'] },
    children: [
      {
        path: 'list',
        name: 'FrpList',
        component: () => import('@/pages/frp/list/index.vue'),
        meta: { title: '隧道列表', icon: 'format-vertical-align-left',roleCode: ['admin', 'user'] },
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
