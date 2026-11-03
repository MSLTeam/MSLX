<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue';
import type { PopupVisibleChangeContext } from 'tdesign-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import { useSettingStore, useWebpanelStore } from '@/store';
import ColorContainer from '@/components/color/index.vue';
import STYLE_CONFIG from '@/config/style';

const settingStore = useSettingStore();
const webpanelStore = useWebpanelStore();

const screenWidth = ref(window.innerWidth);
const isMobile = computed(() => screenWidth.value < 480);
const drawerSize = computed(() => (isMobile.value ? '85%' : '408px'));

const updateScreenWidth = () => {
  screenWidth.value = window.innerWidth;
};

const COLOR_OPTIONS = ['default', 'cyan', 'green', 'yellow', 'orange', 'red', 'pink', 'purple', 'dynamic'];

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

// 跟随系统
const isAutoMode = computed({
  get: () => formData.value.mode === 'auto',
  set: (val) => {
    formData.value.mode = val ? 'auto' : 'light';
  },
});

// 手动模式
const isDarkMode = computed({
  get: () => formData.value.mode === 'dark',
  set: (val) => {
    if (!isAutoMode.value) {
      formData.value.mode = val ? 'dark' : 'light';
    }
  },
});

if (isMobile.value && formData.value.layout === 'side') {
  formData.value.layout = 'top';
}
const isColoPickerDisplay = ref(false);

const showSettingPanel = computed({
  get: () => settingStore.showSettingPanel,
  set: (newVal) => settingStore.updateConfig({ showSettingPanel: newVal }),
});

const onAutoModeChange = (val: boolean) => {
  settingStore.setThemeCustomized(true);
  isAutoMode.value = val;
  settingStore.updateConfig({ mode: formData.value.mode });
};

const onDarkModeChange = (val: boolean) => {
  settingStore.setThemeCustomized(true);
  isDarkMode.value = val;
  settingStore.updateConfig({ mode: formData.value.mode });
};

const onCustomThemeChange = (val: boolean) => {
  settingStore.setThemeCustomized(true);
  formData.value.enableCustomTheme = val;
  settingStore.updateConfig({ enableCustomTheme: val });
};

const onBrandThemeChange = (val: string) => {
  settingStore.setThemeCustomized(true);
  formData.value.brandTheme = val;
  settingStore.updateConfig({ brandTheme: val });
};

const onLayoutChange = (val: 'side' | 'top') => {
  settingStore.setThemeCustomized(true);
  formData.value.layout = val;
  settingStore.updateConfig({ layout: val });
};

const onExpandMutexChange = (val: boolean) => {
  settingStore.setThemeCustomized(true);
  formData.value.expandMutex = val;
  settingStore.updateConfig({ expandMutex: val });
};

const changeColor = (hex: string) => {
  settingStore.setThemeCustomized(true);
  formData.value.brandTheme = hex;
  settingStore.updateConfig({ brandTheme: hex });
};

const handleResetDefault = () => {
  settingStore.resetToDefault(webpanelStore.settings);
  formData.value = { ...initStyleConfig() };
  MessagePlugin.success('已恢复为面板默认主题样式');
};

watch(
  () => settingStore.showSettingPanel,
  (val) => {
    if (val) {
      formData.value = { ...initStyleConfig() };
    }
  },
);

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
        <!-- 主题模式设置 -->
        <section>
          <div class="text-[13px] font-bold text-[var(--td-text-color-secondary)] mb-4 tracking-widest uppercase">
            主题模式
          </div>
          <div class="flex flex-col gap-3">
            <div
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">跟随系统</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">自动切换明暗外观</span>
              </div>
              <t-switch v-model="isAutoMode" size="large" @change="onAutoModeChange" />
            </div>

            <div
              v-show="!isAutoMode"
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600 animate-fade-in"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">暗黑模式</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">手动开启或关闭</span>
              </div>
              <t-switch v-model="isDarkMode" size="large" @change="onDarkModeChange" />
            </div>
          </div>
        </section>

        <!-- 个性化设置 -->
        <section>
          <div class="text-[13px] font-bold text-[var(--td-text-color-secondary)] mb-4 tracking-widest uppercase">
            个性化
          </div>
          <div
            class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
          >
            <div class="flex flex-col">
              <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">开启背景美化</span>
              <span class="text-[11px] text-zinc-400 mt-0.5">启用毛玻璃卡片与自定义壁纸</span>
            </div>
            <t-switch v-model="formData.enableCustomTheme" size="large" @change="onCustomThemeChange" />
          </div>
        </section>

        <!-- 🚀 颜值升级：带背景托盘与光环聚焦的主题色面板 -->
        <section>
          <div class="text-[13px] font-bold text-[var(--td-text-color-secondary)] mb-4 tracking-widest uppercase">
            主题色
          </div>
          <div class="p-5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50">
            <div class="flex flex-wrap gap-4 items-center">
              <div
                v-for="item in COLOR_OPTIONS.slice(0, -1)"
                :key="item"
                class="relative flex items-center justify-center w-5 h-5 rounded-full cursor-pointer transition-all duration-300"
                :class="[
                  formData.brandTheme === item
                    ? 'ring-2 ring-offset-2 ring-offset-[#f8fafc] dark:ring-offset-[#18181b] ring-[var(--color-primary)] scale-110'
                    : 'hover:scale-125 hover:shadow-sm',
                ]"
                @click="onBrandThemeChange(item)"
              >
                <!-- 色块 -->
                <color-container :value="item" class="!w-full !h-full !rounded-full !border-none" />
              </div>

              <!-- 分割线 -->
              <div class="w-[1px] h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>

              <!-- 自定义拾色器按钮 -->
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
                <div
                  class="dynamic-color-btn relative flex items-center justify-center w-5 h-5 rounded-full cursor-pointer transition-all duration-300"
                  :class="[
                    formData.brandTheme === COLOR_OPTIONS[COLOR_OPTIONS.length - 1]
                      ? 'ring-2 ring-offset-2 ring-offset-[#f8fafc] dark:ring-offset-[#18181b] ring-[var(--color-primary)] scale-110'
                      : 'hover:scale-125 hover:shadow-sm',
                  ]"
                >
                  <div
                    class="w-full h-full rounded-full border border-zinc-200/50 dark:border-zinc-600/50"
                    style="
                      background: conic-gradient(
                        from 180deg,
                        #ff0000,
                        #ff8000,
                        #ffff00,
                        #00ff00,
                        #00ffff,
                        #0000ff,
                        #8000ff,
                        #ff00ff,
                        #ff0000
                      );
                    "
                  ></div>
                </div>
              </t-popup>
            </div>
          </div>
        </section>

        <section>
          <div class="text-[13px] font-bold text-[var(--td-text-color-secondary)] mb-4 tracking-widest uppercase">
            导航布局
          </div>
          <div class="flex flex-col gap-3">
            <div
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">当前布局</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">选择侧边栏或顶部导航</span>
              </div>

              <!-- 滑动底座 -->
              <div
                class="relative flex items-center bg-zinc-200/60 dark:bg-zinc-800/80 rounded-lg p-1 w-[130px] h-[34px]"
              >
                <!-- 动态滑块 -->
                <div
                  class="absolute top-1 bottom-1 w-[calc(50%-4px)] bg-white dark:bg-zinc-600 rounded-md shadow-sm transition-transform duration-300 ease-out"
                  :class="formData.layout === 'top' ? 'translate-x-full' : 'translate-x-0'"
                ></div>

                <!-- 侧边栏按钮 -->
                <div
                  class="relative z-10 flex-1 flex items-center justify-center text-[12px] font-medium rounded-md cursor-pointer transition-colors duration-300 select-none"
                  :class="
                    formData.layout === 'side'
                      ? 'text-zinc-800 dark:text-zinc-100'
                      : 'text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300'
                  "
                  @click="onLayoutChange('side')"
                >
                  侧边栏
                </div>
                <!-- 顶栏按钮 -->
                <div
                  class="relative z-10 flex-1 flex items-center justify-center text-[12px] font-medium rounded-md cursor-pointer transition-colors duration-300 select-none"
                  :class="
                    formData.layout === 'top'
                      ? 'text-zinc-800 dark:text-zinc-100'
                      : 'text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300'
                  "
                  @click="onLayoutChange('top')"
                >
                  顶栏
                </div>
              </div>
            </div>

            <div
              v-show="formData.layout === 'side'"
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600 animate-fade-in"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">自动折叠菜单</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">开启后仅允许展开一个菜单</span>
              </div>
              <t-switch v-model="formData.expandMutex" size="large" @change="onExpandMutexChange" />
            </div>
          </div>
        </section>

        <!-- 访客自定义状态与恢复默认 -->
        <div
          v-if="settingStore.isThemeCustomized"
          class="flex items-center justify-between p-4 rounded-xl bg-amber-50/60 dark:bg-amber-950/20 border border-amber-200/60 dark:border-amber-900/40"
        >
          <div class="flex flex-col">
            <span class="text-[13px] font-bold text-amber-900 dark:text-amber-200">当前已自定义样式</span>
            <span class="text-[11px] text-amber-700/80 dark:text-amber-400/80 mt-0.5">当前使用您偏好的专属外观配置</span>
          </div>
          <t-button variant="outline" theme="warning" size="small" @click="handleResetDefault">
            恢复默认
          </t-button>
        </div>
      </t-form>
    </div>
  </t-drawer>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* 抽屉基础样式穿透 */
:deep(.t-drawer__content-wrapper) {
  @apply !bg-white dark:!bg-zinc-800 !border-l !border-zinc-200/50 dark:!border-zinc-700/50;
}
:deep(.t-drawer__header) {
  @apply !px-6 !py-5 !border-b !border-zinc-100 dark:!border-zinc-700/50;
}
:deep(.t-drawer__header-title) {
  @apply !text-[16px] !font-bold !text-zinc-800 dark:!text-zinc-100;
}
:deep(.t-drawer__body) {
  @apply !p-0;
}

:deep(.t-color-container) {
  @apply !p-0 !border-none;
}

/* 自定义拾色器弹窗 */
:deep(.custom-color-picker) {
  @apply !bg-white dark:!bg-zinc-800 !border !border-zinc-200/50 dark:!border-zinc-700/50 !shadow-2xl !rounded-2xl;
}
:deep(.custom-color-picker .t-color-picker__panel) {
  @apply !bg-transparent;
}

/* 淡入动画 */
@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(-4px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
.animate-fade-in {
  animation: fadeIn 0.2s ease-out forwards;
}
</style>
