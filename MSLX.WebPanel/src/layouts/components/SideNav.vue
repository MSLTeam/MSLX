<template>
  <div :class="sideNavCls">
    <t-menu :class="menuCls" :theme="theme" :value="active" :collapsed="collapsed" :default-expanded="defaultExpanded">
      <template #logo>
        <span
          v-if="showLogo"
          :class="`${prefix}-side-nav-logo-wrapper`"
          :style="
            collapsed
              ? 'display: flex; justify-content: center; width: 100%;'
              : 'display: flex; align-items: center; justify-content: flex-start; width: 100%;'
          "
          @click="goHome"
        >
          <img
            style="width: 32px; margin-right: 8px"
            :src="CustomLogo"
            :class="`${prefix}-side-nav-logo-img`"
            alt="logo"
          />
          <span
            v-if="!collapsed"
            style="font-size: 18px; font-weight: bold; text-overflow: ellipsis; overflow: hidden; white-space: nowrap"
            :class="`${prefix}-side-nav-logo-text`"
          >
            MSLX 管理中心
          </span>
        </span>
      </template>
      <menu-content :nav-data="menu" />
      <template #operations>
        <span class="version-container"> {{ !collapsed ? 'MSLX-WEBPANEL' : '' }} v{{ pkg.version }} </span>
      </template>
    </t-menu>
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

<style lang="less" scoped></style>
