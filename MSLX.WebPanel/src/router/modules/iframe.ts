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
        path: 'MSL',
        name: 'MSL',
        component: IFrame,
        meta: {
          frameSrc: 'https://www.mslmc.cn',
          frameBlank: true,
          title: 'MSL官网',
        },
      },
    ],
  },
];
