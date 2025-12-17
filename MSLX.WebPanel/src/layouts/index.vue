<template>
  <div class="global-layout-bg" :class="{ 'custom-theme-enabled': enableCustomTheme }">
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

<script setup lang="ts">
import { computed, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useRoute } from 'vue-router';
import { useSettingStore } from '@/store';

import SettingCom from './setting.vue';
import LayoutHeader from './components/LayoutHeader.vue';
import LayoutContent from './components/LayoutContent.vue';
import LayoutSideNav from './components/LayoutSideNav.vue';

import { prefix } from '@/config/global';

import '@/style/layout.less';

const route = useRoute();
const settingStore = useSettingStore();
const setting = storeToRefs(settingStore);

/* --- 自定义主题开关 --- */
const enableCustomTheme = computed(() => settingStore.enableCustomTheme);

const mainLayoutCls = computed(() => [
  {
    't-layout--with-sider': settingStore.showSidebar,
  },
]);

watch(
  () => route.path,
  () => {
    document.querySelector(`.${prefix}-layout`).scrollTo({ top: 0, behavior: 'smooth' });
  },
);
</script>

<style lang="less" scoped>
// 魔法对轰 - css大作战

// 整体容器
.global-layout-bg {
  width: 100%;
  min-height: 100vh;
  transition: background 0.3s ease;
}

// 魔改样式
.global-layout-bg.custom-theme-enabled {
  // 白天背景图
  background: url("@/assets/bg_light_new.jpg") no-repeat center center fixed;
  background-size: cover;
  color: var(--td-text-color-primary);

  // 黑夜背景图
  &.dark,
  :global(html[theme-mode="dark"]) & {
    background-image: url("@/assets/bg_night_new.jpg") !important;
  }

  // 布局骨架透明
  :deep(.t-layout),
  :deep(.t-layout__content),
  :deep(.t-content) {
    background: transparent !important;
  }

  // 强制删除内部背景
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

    // 重置颜色变量
    --td-bg-color-container: transparent !important;
    --td-bg-color-secondarycontainer: transparent !important;
    --td-component-stroke: transparent !important;
    --td-gray-color-13: transparent !important;
  }

  // 暗黑模式菜单背景
  :deep(.t-menu--dark) {
    background: transparent !important;
    --td-bg-color-container: transparent !important;
  }


  // 白天小卡片样式
  :deep(.t-layout__sider),
  :deep(.t-aside),
  :deep(.t-layout__header),
  :deep(.t-header),
  :deep(.t-card) {
    /* 白天：40% 白色 */
    background-color: rgba(255, 255, 255, 0.4) !important;
    border: 1px solid rgba(255, 255, 255, 0.3) !important;

    /* 禁用磨砂 */
    backdrop-filter: none !important;
    -webkit-backdrop-filter: none !important;

    transition: background-color 0.3s, border-color 0.3s;
  }
  :deep(.t-input) {
    background-color: rgba(255, 255, 255, 0.4) !important;
    border-color: rgba(255, 255, 255, 0.5) !important;
  }

  // 黑暗小卡片样式
  &.dark,
  :global(html[theme-mode="dark"]) & {

    :deep(.t-layout__sider),
    :deep(.t-aside),
    :deep(.t-layout__header),
    :deep(.t-header),
    :deep(.t-card) {
      /* 黑夜：60% 黑色 - 保证文字可读性 */
      background-color: rgba(20, 20, 20, 0.6) !important;

      /* 边框：极淡白色 */
      border: 1px solid rgba(255, 255, 255, 0.08) !important;
    }
    :deep(.t-input) {
      background-color: transparent !important;
      border-color: var(--td-component-border) !important;
    }
  }
  :deep(.md-editor-preview blockquote) {
    background-color: color-mix(in srgb, var(--md-theme-quote-bg-color), transparent 50%) !important;
  }
}
</style>
