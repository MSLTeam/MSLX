import Layout from '@/layouts/index.vue';
import DashboardIcon from '@/assets/assets-slide-dashboard.svg';
import BaseIcon from '@/assets/assets-slide-dashboard.svg';

export default [
  {
    path: '/dashboard',
    component: Layout,
    redirect: '/dashboard/base',
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
        meta: { title: '概览直达', hidden: true },
      },
    ],
  },
  {
    path: '/instance',
    component: Layout,
    // redirect: '/instance/list',
    name: 'instance',
    meta: { title: '服务端管理', icon: 'root-list' },
    children: [
      {
        path: 'list',
        name: 'InstanceList',
        component: () => import('@/pages/instance/createInstance/index.vue'),
        meta: { title: '创建服务端' },
      },
      {
        path: 'detail',
        name: 'DashboardDetail',
        component: () => import('@/pages/dashboard/detail/index.vue'),
        meta: { title: '统计报表' },
      },
    ],
  },
];
