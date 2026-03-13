<script setup lang="ts">
import { ref, computed, onMounted, watchEffect, onBeforeUnmount } from 'vue';
import type { PopupVisibleChangeContext } from 'tdesign-vue-next';
import { Color } from 'tvision-color';

import { useSettingStore } from '@/store';
import ColorContainer from '@/components/color/index.vue';
import STYLE_CONFIG from '@/config/style';
import { insertThemeStylesheet, generateColorMap } from '@/config/color';

const settingStore = useSettingStore();

const screenWidth = ref(window.innerWidth);
const isMobile = computed(() => screenWidth.value < 480);
const drawerSize = computed(() => (isMobile.value ? '85%' : '408px'));

const updateScreenWidth = () => {
  screenWidth.value = window.innerWidth;
};

// 🚨 使用 TDesign 原生 Icon 替换缩略图
const LAYOUT_OPTIONS = [
  { value: 'side', text: '侧边栏', icon: 'view-column' },
  { value: 'top', text: '顶栏导航', icon: 'view-agenda' },
];

const COLOR_OPTIONS = ['default', 'cyan', 'green', 'yellow', 'orange', 'red', 'pink', 'purple', 'dynamic'];

// 🚨 使用 TDesign 原生 Icon 替换引入的 SVG
const MODE_OPTIONS = [
  { type: 'auto', text: '跟随系统', icon: 'desktop' },
  { type: 'light', text: '明亮模式', icon: 'sunny' },
  { type: 'dark', text: '暗黑模式', icon: 'moon' },
];

const initStyleConfig = () => {
  const styleConfig = { ...STYLE_CONFIG };
  for (const key in styleConfig) {
    if (Object.prototype.hasOwnProperty.call(styleConfig, key)) {
      // @ts-ignore
      styleConfig[key] = settingStore[key];
    }
  }
  return styleConfig;
};

const formData = ref({ ...initStyleConfig() });
if (isMobile.value && formData.value.layout === 'side') {
  formData.value.layout = 'top';
}
const isColoPickerDisplay = ref(false);

const showSettingPanel = computed({
  get: () => settingStore.showSettingPanel,
  set: (newVal) => settingStore.updateConfig({ showSettingPanel: newVal }),
});

const changeColor = (hex: string) => {
  const newPalette = Color.getPaletteByGradation({ colors: [hex], step: 10 })[0];
  const { mode } = settingStore;
  const colorMap = generateColorMap(hex, newPalette, mode as 'light' | 'dark');

  settingStore.addColor({ [hex]: colorMap });
  settingStore.updateConfig({ ...formData.value, brandTheme: hex });
  insertThemeStylesheet(hex, colorMap, mode as 'light' | 'dark');
};

onMounted(() => {
  const dynamicBtn = document.querySelector('.dynamic-color-btn');
  if (dynamicBtn) {
    dynamicBtn.addEventListener('click', () => {
      isColoPickerDisplay.value = true;
    });
  }
  window.addEventListener('resize', updateScreenWidth);
});

onBeforeUnmount(() => {
  window.removeEventListener('resize', updateScreenWidth);
});

const onPopupVisibleChange = (visible: boolean, context: PopupVisibleChangeContext) => {
  if (!visible && context.trigger === 'document') {
    isColoPickerDisplay.value = visible;
  }
};

const handleCloseDrawer = () => {
  settingStore.updateConfig({ showSettingPanel: false });
};

watchEffect(() => {
  settingStore.updateConfig({
    mode: formData.value.mode,
    layout: formData.value.layout,
    brandTheme: formData.value.brandTheme,
    enableCustomTheme: formData.value.enableCustomTheme,
  });
});
</script>

<template>
  <t-drawer
    v-model:visible="showSettingPanel"
    :size="drawerSize"
    :footer="false"
    header="面板样式"
    :close-btn="true"
    class="setting-drawer-container"
    @close-btn-click="handleCloseDrawer"
  >
    <div class="p-6 sm:p-8 space-y-10 pb-24">
      <t-form ref="form" :data="formData" label-align="left" class="space-y-10">

        <section>
          <div class="setting-title">主题模式</div>
          <t-radio-group v-model="formData.mode" class="custom-radio-group">
            <t-radio-button v-for="item in MODE_OPTIONS" :key="item.type" :value="item.type" class="icon-card-radio">
              <div class="flex flex-col items-center justify-center gap-2">
                <t-icon :name="item.icon" class="text-[28px] transition-transform duration-300 group-hover:scale-110" />
                <span class="text-[13px] font-medium">{{ item.text }}</span>
              </div>
            </t-radio-button>
          </t-radio-group>
        </section>

        <section>
          <div class="setting-title">个性化</div>
          <div class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-[var(--td-component-border)] transition-colors hover:border-zinc-300 dark:hover:border-zinc-600">
            <div class="flex flex-col">
              <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">开启背景美化</span>
              <span class="text-[11px] text-zinc-400 mt-0.5">启用毛玻璃卡片与自定义壁纸</span>
            </div>
            <t-switch v-model="formData.enableCustomTheme" />
          </div>
        </section>

        <section>
          <div class="setting-title">主题色</div>
          <t-radio-group v-model="formData.brandTheme" class="color-radio-group flex-wrap">
            <t-radio-button
              v-for="item in COLOR_OPTIONS.slice(0, -1)"
              :key="item"
              :value="item"
              class="color-dot-wrapper"
            >
              <color-container :value="item" />
            </t-radio-button>

            <t-popup
              destroy-on-close
              placement="bottom-right"
              trigger="click"
              :visible="isColoPickerDisplay"
              :overlay-style="{ padding: 0 }"
              @visible-change="onPopupVisibleChange"
            >
              <template #content>
                <t-color-picker-panel
                  class="custom-color-picker"
                  :on-change="changeColor"
                  :color-modes="['monochrome']"
                  format="HEX"
                  :swatch-colors="[]"
                />
              </template>
              <t-radio-button
                :value="COLOR_OPTIONS[COLOR_OPTIONS.length - 1]"
                class="color-dot-wrapper dynamic-color-btn"
              >
                <color-container :value="COLOR_OPTIONS[COLOR_OPTIONS.length - 1]" />
              </t-radio-button>
            </t-popup>
          </t-radio-group>
        </section>

        <section>
          <div class="setting-title">导航布局</div>
          <t-radio-group v-model="formData.layout" class="custom-radio-group">
            <t-radio-button v-for="item in LAYOUT_OPTIONS" :key="item.value" :value="item.value" class="icon-card-radio">
              <div class="flex flex-col items-center justify-center gap-2">
                <t-icon :name="item.icon" class="text-[28px] transition-transform duration-300 group-hover:scale-110" />
                <span class="text-[13px] font-medium">{{ item.text }}</span>
              </div>
            </t-radio-button>
          </t-radio-group>
        </section>

      </t-form>
    </div>
  </t-drawer>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* ================== 抽屉基础样式穿透 ================== */
:deep(.t-drawer__content-wrapper) {
  @apply !bg-white dark:!bg-zinc-800 !border-l !border-zinc-200/50 dark:!border-zinc-700/50;
}

:deep(.t-drawer__header) {
  @apply !px-6 !py-5 !border-b !border-zinc-100 dark:!border-zinc-700/50;
}
:deep(.t-drawer__header-title) {
  @apply !text-[16px] !font-bold !text-zinc-800 dark:!text-zinc-100;
}

:deep(.t-drawer__body) { @apply !p-0; }

/* ================== 小标题 ================== */
.setting-title {
  @apply text-[13px] font-bold text-[var(--td-text-color-secondary)] mb-4 tracking-widest uppercase;
}

/* ================== 单选组通用 ================== */
.custom-radio-group, .color-radio-group {
  @apply !flex !w-full !p-0 !border-none !bg-transparent !gap-3;
}

/* ================== Icon 卡片式单选 ================== */
:deep(.icon-card-radio) {
  @apply !flex-1 !h-auto !p-4 !rounded-xl !border-2 !border-zinc-100 dark:!border-zinc-700/50 !bg-zinc-50/50 dark:!bg-zinc-900/30 !text-zinc-500 dark:!text-zinc-400 !transition-all !duration-300;
}

/* 强行抹除内部 label 的 padding */
:deep(.icon-card-radio .t-radio-button__label) {
  @apply !px-0 !w-full;
}

/* Hover 状态 */
:deep(.icon-card-radio:hover:not(.t-is-checked)) {
  @apply !border-zinc-300 dark:!border-zinc-500 !text-zinc-700 dark:!text-zinc-200;
}

/* 选中状态*/
:deep(.icon-card-radio.t-is-checked) {
  @apply !border-[var(--color-primary)] !bg-[var(--color-primary-light)]/15 dark:!bg-[var(--color-primary)]/10 !text-[var(--color-primary)] !shadow-sm !scale-[1.02];
}

:deep(.color-dot-wrapper) {
  @apply !h-auto !p-1.5 !rounded-full !border-2 !border-transparent !bg-transparent !transition-all duration-300;
}

:deep(.color-dot-wrapper .t-radio-button__label) {
  @apply !p-0;
}

:deep(.color-dot-wrapper:hover:not(.t-is-checked)) {
  @apply !bg-[var(--td-bg-color-secondarycontainer)]/50;
}

:deep(.color-dot-wrapper.t-is-checked) {
  @apply !border-[var(--color-primary)] !scale-110 !shadow-md;
}

/* ================== 自定义拾色器弹窗 ================== */
:deep(.custom-color-picker) {
  @apply !bg-white dark:!bg-zinc-800 !border !border-zinc-200/50 dark:!border-zinc-700/50 !shadow-2xl !rounded-2xl;
}
:deep(.custom-color-picker .t-color-picker__panel) {
  @apply !bg-transparent;
}
</style>
