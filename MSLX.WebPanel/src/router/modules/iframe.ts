import Layout from '@/layouts/index.vue';

const IFrame = () => import('@/layouts/components/FrameBlank.vue');

export default [
  {
    path: '/frame',
    name: 'Frame',
    component: Layout,
    redirect: '/frame/doc',
    meta: {
      icon: 'internet',
      title: '链接',
    },

    children: [
      {
        path: 'MSLX',
        name: 'MSLX',
        component: IFrame,
        meta: {
          frameSrc: 'https://mslx.mslmc.cn',
          frameBlank: true,
          title: 'MSLX 文档',
          icon: 'book',
        },
      },
      {
        path: 'MSLUser',
        name: 'MSLUser',
        component: IFrame,
        meta: {
          frameSrc: 'https://user.mslmc.net',
          frameBlank: true,
          title: 'MSL 用户中心',
          icon: 'user-arrow-left',
        },
      },
    ],
  },
];
