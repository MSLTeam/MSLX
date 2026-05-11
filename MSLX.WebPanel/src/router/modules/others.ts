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
      roleCode: ['admin', 'user'],
    },
    children: [
      {
        path: 'profile',
        name: 'settings',
        component: () => import('@/pages/settings/index.vue'),
        meta: { title: '基础设置', icon: 'system-setting', roleCode: ['admin', 'user'] },
      },
      {
        path: 'plugins',
        name: 'plugins',
        component: () => import('@/pages/plugins/pluginsManager/index.vue'),
        meta: { title: '插件管理', icon: 'terminal', roleCode: ['admin'] },
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
      roleCode: ['admin', 'user'],
    },
    children: [
      {
        path: '',
        name: 'about',
        component: () => import('@/pages/about/index.vue'),
        meta: { title: '关于面板', hidden: true, roleCode: ['admin', 'user'] },
      },
    ],
  },
];
