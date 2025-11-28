import Layout from '@/layouts/index.vue';

// 这里放隐藏的页面
export default [
  {
    path: '/frp/console',
    component: Layout,
    meta: { hidden: true },
    children: [
      {
        path: ':id',
        name: 'FrpConsole',
        component: () => import('@/pages/frp/console/index.vue'),
        meta: {
          title: '隧道控制台',
          hidden: true,
          activeMenu: '/frp/list'
        },
      },
    ],
  },
]
