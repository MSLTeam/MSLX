<script setup lang="ts">
import { computed, onMounted, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useRoute } from 'vue-router';
import { useSettingStore } from '@/store';

import SettingCom from './setting.vue';
import LayoutHeader from './components/LayoutHeader.vue';
import LayoutContent from './components/LayoutContent.vue';
import LayoutSideNav from './components/LayoutSideNav.vue';

import { prefix } from '@/config/global';
import '@/style/layout.less';

import defaultLightBg from '@/assets/bg_light_new.jpg';
import defaultDarkBg from '@/assets/bg_night_new.jpg';
import { useWebpanelStore } from '@/store/modules/webpanel';
import { useUserStore } from '@/store';

const route = useRoute();
const settingStore = useSettingStore();
const userStore = useUserStore();
const webpanelStore = useWebpanelStore();
const setting = storeToRefs(settingStore);

const getImageUrl = (fileName: string, defaultImg: string) => {
  if (!fileName) return defaultImg;
  if (fileName.startsWith('http')) return fileName;

  const baseUrl = userStore.baseUrl || window.location.origin;
  return `${baseUrl}/api/static/images/${fileName}`;
};
const customThemeVars = computed(() => {
  const s = webpanelStore.settings;

  const lightBg = getImageUrl(s.webPanelStyleLightBackground, defaultLightBg);
  const darkBg = getImageUrl(s.webPanelStyleNightBackground, defaultDarkBg);

  return {
    '--bg-img-light': `url('${lightBg}')`,
    '--bg-img-dark': `url('${darkBg}')`,
    '--bg-op-light': s.webPanelStyleLightBackgroundOpacity,
    '--bg-op-dark': s.webPanelStyleDarkBackgroundOpacity,
    '--comp-op-light': s.webPanelStyleLightComponentsOpacity,
    '--comp-op-dark': s.webPanelStyleDarkComponentsOpacity,
    '--term-blur-light': '5px',
    '--term-blur-dark': '5px',
  };
});

const enableCustomTheme = computed(() => settingStore.enableCustomTheme);

const mainLayoutCls = computed(() => [{ 't-layout--with-sider': settingStore.showSidebar }]);

onMounted(() => {
  webpanelStore.fetchSettings();
});

watch(
  () => route.path,
  () => {
    document.querySelector(`.${prefix}-layout`).scrollTo({ top: 0, behavior: 'smooth' });
  },
);
</script>

<template>
  <div class="global-layout-bg" :class="{ 'custom-theme-enabled': enableCustomTheme }" :style="customThemeVars">
    <template v-if="setting.layout.value === 'side'">
      <t-layout key="side" :class="mainLayoutCls">
        <t-aside><layout-side-nav /></t-aside>
        <t-layout>
          <t-header><layout-header /></t-header>
          <t-content><layout-content /></t-content>
        </t-layout>
      </t-layout>
    </template>

    <template v-else>
      <t-layout key="no-side">
        <t-header><layout-header /> </t-header>
        <t-layout :class="mainLayoutCls">
          <layout-side-nav />
          <layout-content />
        </t-layout>
      </t-layout>
    </template>
    <setting-com />
  </div>
</template>

<style lang="less" scoped>
.global-layout-bg {
  width: 100%;
  min-height: 100vh;
  position: relative;
  z-index: 1;
  background-color: var(--td-bg-color-page);
  transition: background-color 0.3s ease;
}

.global-layout-bg.custom-theme-enabled {
  color: var(--td-text-color-primary);

  // 使用伪元素控制背景图透明度
  &::before {
    content: '';
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    z-index: -1;
    background-repeat: no-repeat;
    background-position: center center;
    background-size: cover;

    background-image: var(--bg-img-light);
    opacity: var(--bg-op-light);
    transition:
      background-image 0.3s ease,
      opacity 0.3s ease;
  }

  &.dark::before,
  :global(html[theme-mode='dark']) &::before {
    background-image: var(--bg-img-dark) !important;
    opacity: var(--bg-op-dark) !important;
  }

  // 布局透明
  :deep(.t-layout),
  :deep(.t-layout__content),
  :deep(.t-content) {
    background: transparent !important;
  }

  // 清除内部背景
  :deep(.t-menu),
  :deep(.t-head-menu),
  :deep(.t-default-menu),
  :deep(.t-default-menu__inner),
  :deep(.t-head-menu__inner),
  :deep(.t-menu__scroll),
  :deep(.mslx-webpanel-side-nav),
  :deep(.mslx-webpanel-header-layout),
  :deep(.t-card__header),
  :deep(.t-card__footer),
  :deep(.t-card__body) {
    background: transparent !important;
    box-shadow: none !important;
    --td-bg-color-container: transparent !important;
    --td-bg-color-secondarycontainer: transparent !important;
    --td-component-stroke: transparent !important;
    --td-gray-color-13: transparent !important;
  }

  :deep(.t-menu--dark) {
    background: transparent !important;
    --td-bg-color-container: transparent !important;
  }

  // 白天组件样式
  :deep(.t-layout__sider),
  :deep(.t-aside),
  :deep(.t-layout__header),
  :deep(.t-header),
  :deep(.t-card),
  :deep(.design-card),
  :deep(.t-input) {
    background-color: rgba(255, 255, 255, var(--comp-op-light)) !important;
    border: 1px solid rgba(255, 255, 255, 0.3) !important;
    backdrop-filter: none !important;
    -webkit-backdrop-filter: none !important;
    transition:
      background-color 0.3s,
      border-color 0.3s;
  }
  :deep(.t-input) {
    border-color: rgba(255, 255, 255, 0.5) !important;
  }
  // 终端
  :deep(.terminal-wrapper){
    background-color: rgba(255, 255, 255, var(--comp-op-light)) !important;
    border: 1px solid rgba(255, 255, 255, 0.3) !important;
    backdrop-filter: blur(var(--term-blur-light)) !important;
    -webkit-backdrop-filter: blur(var(--term-blur-light)) !important;
  }

  // 黑夜组件样式
  &.dark,
  :global(html[theme-mode='dark']) & {
    :deep(.t-layout__sider),
    :deep(.t-aside),
    :deep(.t-layout__header),
    :deep(.t-header),
    :deep(.design-card),
    :deep(.t-card) {
      background-color: rgba(20, 20, 20, var(--comp-op-dark)) !important;
      border: 1px solid rgba(255, 255, 255, 0.08) !important;
    }
    :deep(.t-input) {
      background-color: transparent !important;
      border-color: var(--td-component-border) !important;
    }
    // 终端
    :deep(.terminal-wrapper){
      background-color: rgba(20, 20, 20, var(--comp-op-dark)) !important;
      border: 1px solid rgba(255, 255, 255, 0.08) !important;
      backdrop-filter: blur(var(--term-blur-dark)) !important;
      -webkit-backdrop-filter: blur(var(--term-blur-dark)) !important;
      --td-component-stroke: rgba(255, 255, 255, 0.1) !important;
    }
  }

  :deep(.md-editor-preview blockquote) {
    background-color: color-mix(in srgb, var(--md-theme-quote-bg-color), transparent 50%) !important;
  }
}
</style>
