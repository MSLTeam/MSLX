import Layout from '@/layouts/index.vue';
import DashboardIcon from '@/assets/assets-slide-dashboard.svg';
import BaseIcon from '@/assets/assets-slide-dashboard.svg';

export default [
  {
    path: '/dashboard-base',
    component: Layout,
    redirect: '/dashboard-base/index',
    name: 'dashboardBaseSingle',
    meta: {
      title: '概览直达',
      icon: BaseIcon,
    },
    children: [
      {
        path: 'index',
        name: 'DashboardBaseIndex',
        component: () => import('@/pages/dashboard/base/index.vue'),
        meta: { title: '概览直达', hidden: true },
      },
    ],
  },
  {
    path: '/dashboard',
    component: Layout,
    redirect: '/dashboard/base',
    name: 'dashboard',
    meta: { title: '仪表盘', icon: DashboardIcon },
    children: [
      {
        path: 'base',
        name: 'DashboardBase',
        component: () => import('@/pages/dashboard/base/index.vue'),
        meta: { title: '概览仪表盘' },
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
