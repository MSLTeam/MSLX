<template>
  <div :class="sideNavCls">
    <div
      class="design-card h-full bg-white dark:bg-zinc-800 transition-all duration-300 relative z-40"
      :class="[
        settingStore.enableCustomTheme
          ? 'border-r border-white/20 dark:border-white/5'
          : 'border-r border-white/20 dark:border-zinc-700/60 shadow-[1px_0_12px_rgba(0,0,0,0.02)] dark:shadow-none'
      ]"
    >

      <t-menu :class="[menuCls, 'sidebar-menu-clear']" :theme="theme" :value="active" :collapsed="collapsed" :default-expanded="defaultExpanded">
        <template #logo>
          <div
            v-if="showLogo"
            class="flex items-center cursor-pointer h-[64px] px-5 overflow-hidden transition-all duration-300 !ml-0 border-b border-transparent"
            :class="collapsed ? 'justify-center px-0' : 'justify-start gap-2.5'"
            @click="goHome"
          >
            <img class="w-7 h-7 object-contain shrink-0" :src="CustomLogo" alt="logo" />
            <span
              v-if="!collapsed"
              class="text-[17px] font-bold truncate text-zinc-800 dark:text-zinc-100 tracking-tight transition-opacity duration-300 leading-none mt-0.5"
            >
              MSLX 管理中心
            </span>
          </div>
        </template>

        <menu-content :nav-data="menu" />

        <template #operations>
          <div class="flex items-center justify-center h-12 text-[11px] font-mono font-medium text-zinc-400 dark:text-zinc-500 tracking-wider">
            {{ !collapsed ? 'MSLX-WEBPANEL ' : '' }}v{{ pkg.version }}
          </div>
        </template>
      </t-menu>

    </div>
    <div :class="`${prefix}-side-nav-placeholder${collapsed ? '-hidden' : ''}`"></div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import type { PropType } from 'vue';
import { useRouter } from 'vue-router';
import union from 'lodash/union';

import { useSettingStore } from '@/store';
import { prefix } from '@/config/global';
import pkg from '@/../package.json';
import type { MenuRoute } from '@/types/interface';
import { getActive, getRoutesExpanded } from '@/router';

import CustomLogo from '@/assets/logo.png';
import MenuContent from './MenuContent.vue';

const MIN_POINT = 992 - 1;

const props = defineProps({
  menu: {
    type: Array as PropType<MenuRoute[]>,
    default: () => [],
  },
  showLogo: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  isFixed: {
    type: Boolean as PropType<boolean>,
    default: true,
  },
  layout: {
    type: String as PropType<string>,
    default: '',
  },
  headerHeight: {
    type: String as PropType<string>,
    default: '64px',
  },
  theme: {
    type: String as PropType<'light' | 'dark'>,
    default: 'light',
  },
  isCompact: {
    type: Boolean as PropType<boolean>,
    default: false,
  },
});

const collapsed = computed(() => useSettingStore().isSidebarCompact);

const active = computed(() => getActive());

const defaultExpanded = computed(() => {
  const path = getActive();
  const parentPath = path.substring(0, path.lastIndexOf('/'));
  const expanded = getRoutesExpanded();
  return union(expanded, parentPath === '' ? [] : [parentPath]);
});

const sideNavCls = computed(() => {
  const { isCompact } = props;
  return [
    `${prefix}-sidebar-layout`,
    {
      [`${prefix}-sidebar-compact`]: isCompact,
    },
  ];
});

const menuCls = computed(() => {
  const { showLogo, isFixed, layout } = props;
  return [
    `${prefix}-side-nav`,
    {
      [`${prefix}-side-nav-no-logo`]: !showLogo,
      [`${prefix}-side-nav-no-fixed`]: !isFixed,
      [`${prefix}-side-nav-mix-fixed`]: layout === 'mix' && isFixed,
    },
  ];
});

const router = useRouter();
const settingStore = useSettingStore();

const autoCollapsed = () => {
  const isCompact = window.innerWidth <= MIN_POINT;
  settingStore.updateConfig({
    isSidebarCompact: isCompact,
  });
};

onMounted(() => {
  autoCollapsed();
  window.onresize = () => {
    autoCollapsed();
  };
});

const goHome = () => {
  router.push('/dashboard/base');
};
</script>

<style scoped>
@reference "@/style/tailwind/index.css";
/* 暗黑模式下，菜单项的背景色 */
&.dark,
:global(html[theme-mode='dark']) & {
  :deep(.sidebar-menu-clear),
  :deep(.t-menu),
  :deep(.t-menu--dark),
  :deep(.t-default-menu__inner) {
    background: transparent !important;
  }
}

/* 强制抹杀 TDesign 的偏移，保证 Logo 的 !ml-0 绝对生效 */
:deep(.t-menu__logo > *) {
  margin-left: 0 !important;
}

:deep(.t-menu) {
  border-right: none !important;
}
:deep(.t-menu__logo) {
  border-bottom: none !important;
  padding: 0 !important;
}
:deep(.t-menu__operations) {
  border-top: none !important;
}
:global(html[theme-mode='dark']) :deep(.t-menu__operations) {
  background: transparent !important;
}

/* 菜单项胶囊化 */
:deep(.t-menu__item) {
  @apply !mx-2 !my-1 !rounded-xl transition-all duration-200 !border-none;
}

/* 隐藏激活状态的右侧小蓝条 */
:deep(.t-menu__item.t-is-active::after) {
  display: none !important;
}

/* 悬浮与激活的绝美质感 */
:deep(.t-menu__item:hover:not(.t-is-active)) {
  @apply !bg-zinc-100 dark:!bg-zinc-700/50 !text-zinc-900 dark:!text-zinc-100;
}

:deep(.t-menu__item.t-is-active) {
  @apply !bg-[var(--color-primary-light)]/20 dark:!bg-[var(--color-primary)]/10 !text-[var(--color-primary)] font-bold;
}

/* 子菜单样式修复 */
:deep(.t-menu__sub) {
  background: transparent !important;
}
:deep(.t-menu__sub .t-menu__item) {
  @apply !mx-3;
}

/* 菜单文字颜色的一致性 */
:global(html[theme-mode='dark']) .design-card :deep(.t-menu__item) {
  color: rgba(255, 255, 255, 0.7);
}

:global(html[theme-mode='dark']) .design-card :deep(.t-menu__item:hover:not(.t-is-active)) {
  color: #ffffff !important;
}

:global(html[theme-mode='dark']) .design-card :deep(.t-menu__item.t-is-active) {
  color: var(--color-primary) !important;
}

/* Layout Sider 的默认底色 */
:global(.t-layout__sider),
:global(.t-aside) {
  background-color: transparent !important;
  background: transparent !important;
}
</style>

