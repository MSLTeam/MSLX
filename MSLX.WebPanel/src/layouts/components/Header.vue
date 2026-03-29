<script setup lang="ts">
import { computed, ref } from 'vue';
import type { PropType } from 'vue';
import { useRouter } from 'vue-router';
import { useSettingStore, useUserStore } from '@/store';
import { getActive } from '@/router';
import { prefix } from '@/config/global';
import type { MenuRoute } from '@/types/interface';

import MenuContent from './MenuContent.vue';
import CustomLogo from '@/assets/logo.png';

const userStore = useUserStore();

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
const mobileMenuVisible = ref(false);

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
    mobileMenuVisible.value = false;
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

// --- 头像特效状态控制 ---
const isAvatarAnimating = ref(false);
const handleAvatarClick = () => {
  if (isAvatarAnimating.value) return;
  isAvatarAnimating.value = true;
  setTimeout(() => {
    isAvatarAnimating.value = false;
  }, 600);
};
</script>
<template>
  <div
    :class="[
      layoutCls,
      'design-card w-full bg-white dark:bg-zinc-800 transition-all duration-300 relative z-50',
      { 'enable-custom-theme': settingStore.enableCustomTheme },
    ]"
  >
    <t-head-menu :class="[menuCls, 'header-menu-clear']" :theme="theme" expand-type="popup" :value="active">
      <template #logo>
        <span
          v-if="showLogo"
          class="flex items-center cursor-pointer mr-1 lg:mr-6 gap-2.5"
          @click="handleNav('/dashboard/base')"
        >
          <img class="w-7 h-7 object-contain shrink-0" :src="CustomLogo" alt="logo" />
          <span
            class="text-[17px] font-bold truncate max-[1012px]:hidden text-[var(--td-text-color-primary)] tracking-tight leading-none mt-0.5"
          >
            MSLX 管理中心
          </span>
        </span>

        <div v-if="layout == 'side'" class="flex lg:hidden ml-1 items-center">
          <t-button theme="default" shape="square" variant="text" class="header-btn" @click="changeCollapsed">
            <t-icon class="text-xl" name="view-list" />
          </t-button>
        </div>

        <div v-if="layout !== 'side'" class="flex lg:hidden ml-1 items-center">
          <t-popup
            v-model="mobileMenuVisible"
            placement="bottom"
            overlay-class-name="mobile-full-width-popup"
            :overlay-style="{ padding: '0', boxShadow: 'none' }"
            attach="body"
          >
            <t-button class="header-btn" theme="default" shape="square" variant="text">
              <t-icon name="app" class="text-[24px]" />
            </t-button>

            <template #content>
              <t-menu
                :value="active"
                :theme="theme"
                expand-mutex
                class="max-h-[calc(100vh-64px)] overflow-y-auto !bg-white/95 dark:!bg-zinc-800/95 backdrop-blur-xl !border-none !border-t !border-zinc-200/50 dark:!border-zinc-700/50 shadow-2xl"
                @change="handleMenuChange"
              >
                <menu-content :nav-data="menu" :is-horizontal="false" />
              </t-menu>
            </template>
          </t-popup>
        </div>
      </template>

      <template v-if="layout !== 'side'" #default>
        <div class="hidden lg:flex flex-1 items-center">
          <menu-content class="header-menu-reset" :nav-data="menu" :is-horizontal="true" />
        </div>
      </template>

      <template #operations>
        <div class="flex items-center gap-1 sm:gap-2">
          <div class="hidden lg:flex items-center gap-1 sm:gap-2">
            <t-tooltip placement="bottom" content="代码仓库">
              <t-button theme="default" shape="square" variant="text" class="header-btn" @click="navToGitHub">
                <t-icon name="logo-github" class="text-[20px]" />
              </t-button>
            </t-tooltip>

            <t-tooltip placement="bottom" content="帮助文档">
              <t-button theme="default" shape="square" variant="text" class="header-btn" @click="navToHelper">
                <t-icon name="help-circle" class="text-[20px]" />
              </t-button>
            </t-tooltip>
          </div>

          <t-popup
            trigger="click"
            placement="bottom-right"
            :overlay-inner-style="{ padding: '0', background: 'transparent', boxShadow: 'none' }"
            attach="body"
          >
            <t-button class="user-profile-btn" theme="default" variant="text">
              <template #icon>
                <img
                  :src="userStore.userInfo.avatar"
                  class="w-8 h-8 rounded-full object-cover ring-2 ring-zinc-100 dark:ring-zinc-700/80 shadow-sm"
                  alt="avatar"
                />
              </template>
              <div
                class="flex items-center text-sm font-bold text-zinc-700 dark:text-zinc-200 ml-1 truncate max-w-[100px]"
              >
                {{ userStore.userInfo.name || userStore.userInfo.username || '用户' }}
              </div>
              <template #suffix><t-icon name="chevron-down" class="text-zinc-400 text-xs ml-0.5" /></template>
            </t-button>

            <template #content>
              <div
                class="flex flex-col w-[240px] bg-white dark:bg-zinc-800 rounded-xl shadow-xl border border-zinc-100 dark:border-zinc-700/60 overflow-hidden mt-1"
              >
                <div
                  class="px-4 py-4 flex items-center gap-3 border-b border-zinc-100 dark:border-zinc-700/60 bg-zinc-50/50 dark:bg-zinc-800/50"
                >
                  <div class="relative shrink-0 group cursor-pointer" @click="handleAvatarClick">
                    <div
                      class="absolute inset-0 rounded-full z-0 pointer-events-none transition-opacity"
                      :class="isAvatarAnimating ? 'animate-magic-burst' : 'opacity-0'"
                      style="background: radial-gradient(circle, var(--color-primary-light) 0%, transparent 70%)"
                    ></div>

                    <img
                      :src="userStore.userInfo.avatar"
                      class="w-10 h-10 rounded-full object-cover ring-2 ring-[var(--color-primary)]/30 shadow-sm transition-all duration-300 relative z-10"
                      :class="[isAvatarAnimating ? 'animate-jelly-pop' : 'group-hover:rotate-6 group-hover:scale-105']"
                      alt="avatar"
                    />
                  </div>
                  <div class="flex flex-col min-w-0 flex-1">
                    <span class="text-sm font-bold text-zinc-800 dark:text-zinc-100 truncate">
                      {{ userStore.userInfo.name || userStore.userInfo.username || '未知用户' }}
                    </span>
                    <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1 flex items-center gap-1.5">
                      <span
                        class="inline-block w-1.5 h-1.5 rounded-full"
                        :class="userStore.isAdmin ? 'bg-emerald-500' : 'bg-blue-500'"
                      ></span>
                      {{ userStore.isAdmin ? '管理员' : '普通用户' }}
                    </span>
                  </div>
                </div>

                <div class="p-2 flex flex-col gap-1">
                  <div
                    class="flex items-center px-3 py-2 text-sm text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-700/50 rounded-lg cursor-pointer transition-colors"
                    @click="handleNav('/settings')"
                  >
                    <t-icon name="user-circle" class="text-lg mr-2 opacity-70"></t-icon>
                    <span class="font-medium">个人中心</span>
                  </div>

                  <div
                    class="flex items-center px-3 py-2 text-sm text-red-600 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-500/10 rounded-lg cursor-pointer transition-colors"
                    @click="handleLogout"
                  >
                    <t-icon name="poweroff" class="text-lg mr-2 opacity-70"></t-icon>
                    <span class="font-medium">退出登录</span>
                  </div>
                </div>
              </div>
            </template>
          </t-popup>

          <div class="hidden lg:flex items-center">
            <t-tooltip placement="bottom" content="系统设置">
              <t-button theme="default" shape="square" variant="text" class="header-btn" @click="toggleSettingPanel">
                <t-icon name="setting" class="text-[20px]" />
              </t-button>
            </t-tooltip>
          </div>

          <div class="flex lg:hidden items-center">
            <t-dropdown :min-column-width="140" trigger="click">
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
          </div>
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

// 击穿底色
:deep(.t-menu),
:deep(.t-head-menu),
:deep(.t-menu--dark),
:deep(.t-menu--light),
:deep(.t-head-menu__inner) {
  background: transparent !important;
}

// 击穿边框
:deep(.t-head-menu__inner),
:deep(.t-menu__item) {
  border: none !important;
  box-shadow: none !important;
  outline: none !important;
}

:deep(.t-head-menu__inner) {
  @apply px-4 sm:px-6;
}

// 击穿伪元素
:deep(.t-menu__item::after),
:deep(.t-menu__item::before),
:deep(.t-menu__item.t-is-active::after) {
  display: none !important;
}

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

// 击穿layout底色
:global(.t-layout__header) {
  background-color: transparent !important;
  background: transparent !important;
  border-bottom: none !important;
}

.mslx-webpanel-header-layout {
  height: 100%;
}

/* ================== 移动端下拉菜单 ================== */
:global(.mobile-full-width-popup) {
  position: fixed !important;
  top: 48px !important;
  left: 0 !important;
  width: 100vw !important; /* 满宽 */
  max-width: 100vw !important;
  transform: none !important;
  margin-top: 0 !important;
}

:global(.mobile-full-width-popup .t-default-menu) {
  width: 100% !important;
  max-width: 100% !important;
}

/* 放大移动端菜单项 */
:global(.mobile-full-width-popup .t-menu__item) {
  @apply !h-12 !text-[15px];
}

/* ================== Header 底边线强化 ================== */

/* 没有开启美化时，强制显示一条有存在感的底线 */
.design-card:not(.enable-custom-theme) {
  border-bottom: 1px solid rgba(0, 0, 0, 0.08) !important; /* 亮色模式：淡淡的灰色 */
}

:global(html[theme-mode='dark']) .design-card:not(.enable-custom-theme) {
  border-bottom: 1px solid rgba(255, 255, 255, 0.08) !important; /* 暗黑模式：克制的深色线 */
}

/* 当开启美化时，使用极细的高光线*/
.design-card.enable-custom-theme {
  border-bottom: 1px solid rgba(255, 255, 255, 0.15) !important;
  backdrop-filter: blur(20px);
}

:deep(.t-head-menu) {
  background-color: transparent !important;
}

/* q弹头像特效 */
@keyframes jellyPop {
  0% {
    transform: scale(1);
  }
  30% {
    transform: scale(0.85);
  }
  50% {
    transform: scale(1.15);
  }
  65% {
    transform: scale(0.95);
  }
  80% {
    transform: scale(1.05);
  }
  100% {
    transform: scale(1);
  }
}

@keyframes magicBurst {
  0% {
    box-shadow: 0 0 0 0 var(--color-primary);
    opacity: 0.8;
    transform: scale(1);
  }
  100% {
    box-shadow: 0 0 0 35px transparent;
    opacity: 0;
    transform: scale(1.2);
  }
}

.animate-jelly-pop {
  animation: jellyPop 0.6s cubic-bezier(0.25, 1, 0.5, 1) both;
}

.animate-magic-burst {
  animation: magicBurst 0.6s cubic-bezier(0.1, 0.8, 0.3, 1) both;
}
</style>
