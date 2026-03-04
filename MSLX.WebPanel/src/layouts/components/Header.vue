<script setup lang="ts">
import { computed } from 'vue';
import type { PropType } from 'vue';
import { useRouter } from 'vue-router';
import { useSettingStore,useUserStore } from '@/store';
import { getActive } from '@/router';
import { prefix } from '@/config/global';
import type { MenuRoute } from '@/types/interface';

const userStore = useUserStore();


import MenuContent from './MenuContent.vue';
import CustomLogo from "@/assets/logo.png";

const props = defineProps({
  theme: {
    type: String as PropType<'light' | 'dark'>,
    default: 'light',
  },
  layout: {
    type: String,
    default: 'top',
  },
  showLogo: {
    type: Boolean,
    default: true,
  },
  menu: {
    type: Array as PropType<MenuRoute[]>,
    default: () => [],
  },
  isFixed: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  isCompact: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
  maxLevel: {
    type: Number,
    default: 3,
  },
});

const router = useRouter();
const settingStore = useSettingStore();

const toggleSettingPanel = () => {
  settingStore.updateConfig({
    showSettingPanel: true,
  });
};

const active = computed(() => getActive());

const layoutCls = computed(() => [`${prefix}-header-layout`]);

const menuCls = computed(() => {
  const { isFixed, layout, isCompact } = props;
  return [
    {
      [`${prefix}-header-menu`]: !isFixed,
      [`${prefix}-header-menu-fixed`]: isFixed,
      [`${prefix}-header-menu-fixed-side`]: layout === 'side' && isFixed,
      [`${prefix}-header-menu-fixed-side-compact`]: layout === 'side' && isFixed && isCompact,
    },
  ];
});

const changeCollapsed = () => {
  settingStore.updateConfig({
    isSidebarCompact: !settingStore.isSidebarCompact,
  });
};

const handleNav = (url) => {
  router.push(url);
};

const handleMenuChange = (value: string) => {
  // value = 路由 path
  if (value) {
    router.push(value);
  }
};
const handleLogout = () => {
  router.push({
    path: '/login',
    query: { redirect: encodeURIComponent(router.currentRoute.value.fullPath) },
  });
};

const navToGitHub = () => {
  window.open('https://github.com/MSLTeam/MSLX');
};

const navToHelper = () => {
  window.open('https://mslx.mslmc.cn');
};
</script>
<template>
  <div :class="layoutCls" class="design-card w-full bg-white dark:bg-zinc-800 border-b border-zinc-100 dark:border-zinc-800/80 transition-all duration-300 relative z-50">

    <t-head-menu :class="[menuCls, 'header-menu-clear']" :theme="theme" expand-type="popup" :value="active">
      <template #logo>
        <span v-if="showLogo" class="flex items-center cursor-pointer mr-6 gap-2.5" @click="handleNav('/dashboard/base')">
          <img class="w-7 h-7 object-contain shrink-0" :src="CustomLogo" alt="logo" />
          <span class="text-[17px] font-bold truncate max-[1012px]:hidden text-zinc-800 dark:text-zinc-100 tracking-tight leading-none mt-0.5"> MSLX 管理中心 </span>
        </span>

        <div v-if="layout == 'side'" class="max-[1012px]:flex hidden ml-4 items-center">
          <t-button theme="default" shape="square" variant="text" class="header-btn" @click="changeCollapsed">
            <t-icon class="text-xl" name="view-list" />
          </t-button>
        </div>
      </template>

      <template v-if="layout !== 'side'" #default>
        <menu-content class="flex-1 inline-flex items-center max-[1012px]:hidden header-menu-reset" :nav-data="menu" :is-horizontal="true" />

        <t-popup
          placement="bottom-left"
          class="max-[1012px]:inline-flex hidden items-center"
          :overlay-style="{ padding: '0', width: '100%', marginTop: '2px', boxShadow: 'none' }"
        >
          <t-button class="-ml-4 header-btn" theme="default" shape="square" variant="text">
            <t-icon name="app" />
          </t-button>
          <template #content>
            <t-menu :value="active" :theme="theme" expand-mutex class="w-screen !bg-white dark:!bg-zinc-800 !border-none" @change="handleMenuChange">
              <menu-content :nav-data="menu"/>
            </t-menu>
          </template>
        </t-popup>
      </template>

      <template #operations>
        <div class="flex items-center gap-1 sm:gap-2">

          <t-tooltip placement="bottom" content="代码仓库" class="max-[1012px]:hidden">
            <t-button theme="default" shape="square" variant="text" class="header-btn" @click="navToGitHub">
              <t-icon name="logo-github" class="text-[20px]" />
            </t-button>
          </t-tooltip>

          <t-tooltip placement="bottom" content="帮助文档" class="max-[1012px]:hidden">
            <t-button theme="default" shape="square" variant="text" class="header-btn" @click="navToHelper">
              <t-icon name="help-circle" class="text-[20px]" />
            </t-button>
          </t-tooltip>

          <t-dropdown :min-column-width="140" trigger="click">
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item class="operations-dropdown-item" @click="handleNav('/settings')">
                  <t-icon name="user-circle" class="text-lg mr-2"></t-icon>
                  <span>个人中心</span>
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-item danger-item" @click="handleLogout">
                  <t-icon name="poweroff" class="text-lg mr-2"></t-icon>
                  <span>退出登录</span>
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
            <t-button class="user-profile-btn" theme="default" variant="text">
              <template #icon>
                <img :src="userStore.userInfo.avatar" class="w-8 h-8 rounded-full object-cover ring-2 ring-zinc-100 dark:ring-zinc-700/80 shadow-sm" alt="avatar" />
              </template>
              <div class="flex items-center text-sm font-bold text-zinc-700 dark:text-zinc-200 ml-1">
                {{ userStore.userInfo.name }}
              </div>
              <template #suffix><t-icon name="chevron-down" class="text-zinc-400 text-xs ml-0.5" /></template>
            </t-button>
          </t-dropdown>

          <t-dropdown :min-column-width="140" trigger="click" class="max-[1012px]:flex hidden">
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item class="operations-dropdown-item" @click="navToGitHub">
                  <t-icon name="logo-github" class="text-lg mr-2"></t-icon>
                  <span>代码仓库</span>
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-item mt-1" @click="navToHelper">
                  <t-icon name="help-circle" class="text-lg mr-2"></t-icon>
                  <span>帮助文档</span>
                </t-dropdown-item>
                <t-dropdown-item class="operations-dropdown-item mt-1" @click="toggleSettingPanel">
                  <t-icon name="setting" class="text-lg mr-2"></t-icon>
                  <span>系统设置</span>
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
            <t-button theme="default" shape="square" variant="text" class="header-btn">
              <t-icon name="more" class="text-[20px]" />
            </t-button>
          </t-dropdown>

          <t-tooltip placement="bottom" content="系统设置" class="max-[1012px]:hidden">
            <t-button theme="default" shape="square" variant="text" class="header-btn" @click="toggleSettingPanel">
              <t-icon name="setting" class="text-[20px]" />
            </t-button>
          </t-tooltip>

        </div>
      </template>
    </t-head-menu>
  </div>
</template>

<style lang="less" scoped>
@reference "@/style/tailwind/index.css";

.@{starter-prefix}-header {
  &-menu-fixed {
    position: fixed;
    top: 0;
    z-index: 1001;
    width: 100%;

    &-side {
      left: 232px;
      right: 0;
      z-index: 10;
      width: auto;
      transition: all 0.3s;
      &-compact {
        left: 64px;
      }
    }
  }
}

/* ================== 核弹级洗髓：干掉一切背景和边框 ================== */

/* 1. 强制击穿 .t-menu--dark 和 .t-menu--light 的背景色劫持，让我们的 bg-white 透出来 */
/* ================== Header 彻底透明化 ================== */
:deep(.t-menu),
:deep(.t-head-menu),
:deep(.t-menu--dark),
:deep(.t-menu--light),
:deep(.t-head-menu__inner) {  /* 👈 同样干掉它的内层底色 */
  background: transparent !important;
}

/* 2. 物理消灭所有的边框、轮廓和阴影 */
:deep(.t-head-menu__inner),
:deep(.t-menu__item) {
  border: none !important;
  box-shadow: none !important;
  outline: none !important;
}

:deep(.t-head-menu__inner) {
@apply px-4 sm:px-6;
}

/* 3. 斩草除根：TDesign 自动生成的下划线和边框伪元素统统去死 */
:deep(.t-menu__item::after),
:deep(.t-menu__item::before),
:deep(.t-menu__item.t-is-active::after) {
  display: none !important;
}

/* ================================================================= */

.header-menu-reset {
  :deep(.t-menu__item) {
    min-width: unset;
  @apply px-4 mx-1 rounded-xl transition-all border-none !important;
  }
}

:deep(.header-btn) {
@apply !border-none !bg-transparent hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50 !text-zinc-600 dark:!text-zinc-300 transition-colors !rounded-lg;
}

:deep(.user-profile-btn) {
@apply !border-none !bg-transparent hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50 !px-2 !py-1 !rounded-xl transition-colors !h-auto;
}

/* 修复暗黑模式下菜单文字颜色 */
:global(html[theme-mode='dark']) .design-card {
  :deep(.t-menu__item) {
    color: rgba(255, 255, 255, 0.7);
    &:hover {
      color: #fff;
      background: rgba(255, 255, 255, 0.05) !important;
    }
  }
}

/* ================== 终极修复：击杀父级 Layout 的默认底色 ================== */
:global(.t-layout__header) {
  background-color: transparent !important;
  background: transparent !important;
  border-bottom: none !important;
}

/* 确保内部容器 100% 占满高度，防止漏出缝隙 */
.mslx-webpanel-header-layout {
  height: 100%;
}
</style>
