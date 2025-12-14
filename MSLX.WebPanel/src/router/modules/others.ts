import Layout from '@/layouts/index.vue';

export default [
  {
    path: '/users',
    component: Layout,
    name: 'usersBase',
    meta: {
      title: '用户管理',
      icon: 'user',
    },
    children: [
      {
        path: '',
        name: 'users',
        component: () => import('@/pages/users/index.vue'),
        meta: { title: '用户管理', hidden: true },
      },
    ],
  },
  {
    path: '/settings',
    component: Layout,
    name: 'settingsBase',
    meta: {
      title: '设置',
      icon: 'setting',
    },
    children: [
      {
        path: '',
        name: 'settings',
        component: () => import('@/pages/settings/index.vue'),
        meta: { title: '设置', hidden: true },
      },
    ],
  },
  {
    path: '/about',
    component: Layout,
    name: 'aboutBase',
    meta: {
      title: '关于面板',
      icon: 'info-circle',
    },
    children: [
      {
        path: '',
        name: 'about',
        component: () => import('@/pages/about/index.vue'),
        meta: { title: '关于面板', hidden: true },
      },
    ],
  },
];
