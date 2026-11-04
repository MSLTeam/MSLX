<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { useSettingStore, useUserStore, useWebpanelStore } from '@/store';
import defaultLightBg from '@/assets/bg_light_new.jpg';
import defaultDarkBg from '@/assets/bg_night_new.jpg';
import { SunnyIcon, MoonIcon } from 'tdesign-icons-vue-next';

defineOptions({
  name: 'PluginsPublicLayout',
});

const settingStore = useSettingStore();
const userStore = useUserStore();
const webpanelStore = useWebpanelStore();

// 暗黑模式判断
const isDark = computed(() => settingStore.displayMode === 'dark');

// 快捷明暗模式切换
const toggleTheme = () => {
  settingStore.updateConfig({ mode: isDark.value ? 'light' : 'dark' });
};

const getImageUrl = (fileName: string, defaultImg: string) => {
  if (!fileName) return defaultImg;
  if (fileName.startsWith('http')) return fileName;

  const baseUrl = userStore.baseUrl || window.location.origin;
  return `${baseUrl}/api/static/images/${fileName}`;
};

// 复制自 index.vue 的主题变量注入逻辑
const customThemeVars = computed(() => {
  const s = webpanelStore.settings;

  const lightBg = getImageUrl(s.webPanelStyleLightBackground, defaultLightBg);
  const darkBg = getImageUrl(s.webPanelStyleDarkBackground, defaultDarkBg);

  return {
    '--bg-img-light': `url('${lightBg}')`,
    '--bg-img-dark': `url('${darkBg}')`,
    '--bg-op-light': String(s.webPanelStyleLightBackgroundOpacity ?? 1),
    '--bg-op-dark': String(s.webPanelStyleDarkBackgroundOpacity ?? 1),
    '--comp-op-light': String(s.webPanelStyleLightComponentsOpacity ?? 0.85),
    '--comp-op-dark': String(s.webPanelStyleDarkComponentsOpacity ?? 0.65),
    '--term-blur-light': `${s.webpPanelTerminalBlurLight ?? 5}px`,
    '--term-blur-dark': `${s.webpPanelTerminalBlurDark ?? 5}px`,
  };
});

onMounted(() => {
  webpanelStore.fetchSettings();
});
</script>

<template>
  <div
    class="global-layout-bg public-layout-wrapper custom-theme-enabled"
    :class="{ dark: isDark }"
    :style="customThemeVars"
  >
    <!-- 右上角快捷明暗模式切换 -->
    <div class="public-theme-toggle fixed top-4 right-4 z-50">
      <t-button
        theme="default"
        shape="circle"
        variant="outline"
        class="!w-10 !h-10 !rounded-full !p-0 shadow-sm backdrop-blur-md"
        :title="isDark ? '切换至亮色模式' : '切换至暗色模式'"
        @click="toggleTheme"
      >
        <template #icon>
          <sunny-icon v-if="isDark" class="text-amber-400 text-lg" />
          <moon-icon v-else class="text-slate-600 text-lg" />
        </template>
      </t-button>
    </div>

    <!-- 内容展示区域 -->
    <main class="public-layout-content min-h-screen w-full flex flex-col">
      <router-view />
    </main>
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

body .global-layout-bg.custom-theme-enabled,
.global-layout-bg.custom-theme-enabled {
  color: var(--td-text-color-primary);
  --current-comp-op: var(--comp-op-light);

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
    transition: background-image 0.3s ease, opacity 0.3s ease;
  }

  &.dark::before {
    background-image: var(--bg-img-dark) !important;
    opacity: var(--bg-op-dark) !important;
  }

  // 恢复单选框组件背景变量，防止滑块变透明
  :deep(.t-radio-group) {
    --td-bg-color-container: #ffffff !important;
  }
  &.dark {
    :deep(.t-radio-group) {
      --td-bg-color-container: #3f3f46 !important;
    }
  }

  // 白天组件样式
  :deep(.t-card),
  :deep(.design-card),
  :deep(.t-textarea__inner),
  :deep(.t-input-number),
  :deep(.t-input),
  :deep(.t-select__wrap .t-input) {
    background-color: rgba(255, 255, 255, var(--comp-op-light)) !important;
    border-color: rgba(255, 255, 255, 0.3) !important;

    backdrop-filter: none !important;
    -webkit-backdrop-filter: none !important;
    transition: background-color 0.3s, border-color 0.3s;
  }

  :deep(.t-input),
  :deep(.t-textarea__inner),
  :deep(.t-input-number) {
    border-color: var(--td-component-border) !important;
  }

  // 黑夜组件样式
  &.dark {
    --current-comp-op: var(--comp-op-dark);

    :deep(.design-card),
    :deep(.t-card) {
      background-color: rgba(20, 20, 20, var(--comp-op-dark)) !important;
      border-color: rgba(255, 255, 255, 0.08) !important;
    }

    :deep(.t-input),
    :deep(.t-textarea__inner),
    :deep(.t-input-number),
    :deep(.t-select__wrap .t-input) {
      background-color: transparent !important;
      border-color: var(--td-component-border) !important;
    }
  }
}
</style>
