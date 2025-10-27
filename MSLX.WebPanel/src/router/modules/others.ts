import Layout from '@/layouts/index.vue';

export default [
  {
    path: '/dashboard/about',
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
        component: () => import('@/pages/user/index.vue'),
        meta: { title: '关于面板', hidden: true },
      },
    ],
  },
];
