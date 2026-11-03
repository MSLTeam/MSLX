<script setup lang="ts">
import { ref, computed } from 'vue';
import type { PopupVisibleChangeContext } from 'tdesign-vue-next';
import { useWebpanelStore } from '@/store/modules/webpanel';
import ColorContainer from '@/components/color/index.vue';

const webpanelStore = useWebpanelStore();

const COLOR_OPTIONS = ['default', 'cyan', 'green', 'yellow', 'orange', 'red', 'pink', 'purple', 'dynamic'];

const isColorPickerDisplay = ref(false);

// 跟随系统模式
const isAutoMode = computed({
  get: () => (webpanelStore.settings.webPanelDefaultThemeMode ?? 'auto') === 'auto',
  set: (val: boolean) => {
    webpanelStore.settings.webPanelDefaultThemeMode = val ? 'auto' : 'light';
  },
});

// 手动暗黑模式
const isDarkMode = computed({
  get: () => webpanelStore.settings.webPanelDefaultThemeMode === 'dark',
  set: (val: boolean) => {
    if (!isAutoMode.value) {
      webpanelStore.settings.webPanelDefaultThemeMode = val ? 'dark' : 'light';
    }
  },
});

const currentBrandTheme = computed(() => webpanelStore.settings.webPanelDefaultBrandTheme ?? 'default');
const currentLayout = computed(() => webpanelStore.settings.webPanelDefaultLayout ?? 'side');

const onPopupVisibleChange = (visible: boolean, context: PopupVisibleChangeContext) => {
  if (!visible && context.trigger === 'document') {
    isColorPickerDisplay.value = visible;
  }
};

const changeColor = (hex: string) => {
  webpanelStore.settings.webPanelDefaultBrandTheme = hex;
};
</script>

<template>
  <div
    class="design-card list-item-anim relative flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm transition-all duration-300"
  >
    <t-loading :loading="webpanelStore.loading" show-overlay>
      <div class="p-5 sm:p-6 sm:px-8">
        <!-- 标题区域 -->
        <div
          class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
        >
          <div class="flex items-center gap-3">
            <div
              class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"
            ></div>
            <div class="flex flex-col">
              <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">
                面板默认主题样式
              </h2>
              <span class="text-[11px] sm:text-xs text-[var(--td-text-color-secondary)] mt-1.5 font-medium">
                设置新访客首次进入面板时的默认外观样式。若访问者曾在右上角抽屉主动修改过样式，将优先遵循其本地设置。
              </span>
            </div>
          </div>
        </div>

        <t-form :data="webpanelStore.settings" label-align="top" @submit="webpanelStore.saveSettings">
          <!-- 1. 主题模式 -->
          <div class="flex items-center gap-3 mt-2 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >主题模式</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6">
            <div
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">跟随系统</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">默认自动根据系统外观切换明暗</span>
              </div>
              <t-switch v-model="isAutoMode" size="large" />
            </div>

            <div
              v-show="!isAutoMode"
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600 animate-fade-in"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">暗黑模式</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">关闭跟随系统时默认开启或关闭</span>
              </div>
              <t-switch v-model="isDarkMode" size="large" />
            </div>
          </div>

          <!-- 2. 个性化 -->
          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >个性化</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div class="mb-6">
            <div
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">默认开启背景美化</span>
                <span class="text-[11px] text-zinc-400 mt-0.5"
                  >新访客默认启用毛玻璃卡片与上方配置的自定义壁纸</span
                >
              </div>
              <t-switch v-model="webpanelStore.settings.webPanelDefaultEnableCustomTheme" size="large" />
            </div>
          </div>

          <!-- 3. 主题色 -->
          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >主题色</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div
            class="p-5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 mb-6"
          >
            <div class="flex flex-wrap gap-4 items-center">
              <div
                v-for="item in COLOR_OPTIONS.slice(0, -1)"
                :key="item"
                class="relative flex items-center justify-center w-6 h-6 rounded-full cursor-pointer transition-all duration-300"
                :class="[
                  currentBrandTheme === item
                    ? 'ring-2 ring-offset-2 ring-offset-[#f8fafc] dark:ring-offset-[#18181b] ring-[var(--color-primary)] scale-110'
                    : 'hover:scale-125 hover:shadow-sm',
                ]"
                @click="webpanelStore.settings.webPanelDefaultBrandTheme = item"
              >
                <!-- 色块 -->
                <color-container :value="item" class="!w-full !h-full !rounded-full !border-none" />
              </div>

              <!-- 分割线 -->
              <div class="w-[1px] h-5 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>

              <!-- 自定义拾色器按钮 -->
              <t-popup
                destroy-on-close
                placement="bottom-right"
                trigger="click"
                :visible="isColorPickerDisplay"
                :overlay-style="{ padding: 0 }"
                @visible-change="onPopupVisibleChange"
              >
                <template #content>
                  <t-color-picker-panel
                    class="custom-color-picker"
                    :value="currentBrandTheme"
                    :on-change="changeColor"
                    :color-modes="['monochrome']"
                    format="HEX"
                    :swatch-colors="[]"
                  />
                </template>
                <div
                  class="relative flex items-center justify-center w-6 h-6 rounded-full cursor-pointer transition-all duration-300"
                  :class="[
                    currentBrandTheme === COLOR_OPTIONS[COLOR_OPTIONS.length - 1] || currentBrandTheme.startsWith('#')
                      ? 'ring-2 ring-offset-2 ring-offset-[#f8fafc] dark:ring-offset-[#18181b] ring-[var(--color-primary)] scale-110'
                      : 'hover:scale-125 hover:shadow-sm',
                  ]"
                  @click="isColorPickerDisplay = true"
                >
                  <div
                    class="w-full h-full rounded-full border border-zinc-200/50 dark:border-zinc-600/50"
                    :style="
                      currentBrandTheme.startsWith('#')
                        ? { backgroundColor: currentBrandTheme }
                        : {
                            background:
                              'conic-gradient(from 180deg, #ff0000, #ff8000, #ffff00, #00ff00, #00ffff, #0000ff, #8000ff, #ff00ff, #ff0000)',
                          }
                    "
                  ></div>
                </div>
              </t-popup>

              <span
                v-if="currentBrandTheme.startsWith('#')"
                class="text-xs font-mono text-zinc-500 dark:text-zinc-400 font-medium ml-1"
              >
                {{ currentBrandTheme.toUpperCase() }}
              </span>
            </div>
          </div>

          <!-- 4. 导航布局 -->
          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >导航布局</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div class="flex flex-col gap-3 mb-6">
            <div
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">默认导航布局</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">选择侧边栏或顶部导航</span>
              </div>

              <!-- 滑动底座 -->
              <div
                class="relative flex items-center bg-zinc-200/60 dark:bg-zinc-800/80 rounded-lg p-1 w-[130px] h-[34px]"
              >
                <!-- 动态滑块 -->
                <div
                  class="absolute top-1 bottom-1 w-[calc(50%-4px)] bg-white dark:bg-zinc-600 rounded-md shadow-sm transition-transform duration-300 ease-out"
                  :class="currentLayout === 'top' ? 'translate-x-full' : 'translate-x-0'"
                ></div>

                <!-- 侧边栏按钮 -->
                <div
                  class="relative z-10 flex-1 flex items-center justify-center text-[12px] font-medium rounded-md cursor-pointer transition-colors duration-300 select-none"
                  :class="
                    currentLayout === 'side'
                      ? 'text-zinc-800 dark:text-zinc-100'
                      : 'text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300'
                  "
                  @click="webpanelStore.settings.webPanelDefaultLayout = 'side'"
                >
                  侧边栏
                </div>
                <!-- 顶栏按钮 -->
                <div
                  class="relative z-10 flex-1 flex items-center justify-center text-[12px] font-medium rounded-md cursor-pointer transition-colors duration-300 select-none"
                  :class="
                    currentLayout === 'top'
                      ? 'text-zinc-800 dark:text-zinc-100'
                      : 'text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300'
                  "
                  @click="webpanelStore.settings.webPanelDefaultLayout = 'top'"
                >
                  顶栏
                </div>
              </div>
            </div>

            <div
              v-show="currentLayout === 'side'"
              class="flex items-center justify-between p-4 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-100 dark:border-zinc-700/50 transition-colors hover:border-zinc-300 dark:hover:border-zinc-600 animate-fade-in"
            >
              <div class="flex flex-col">
                <span class="text-[14px] font-bold text-[var(--td-text-color-primary)]">自动折叠菜单</span>
                <span class="text-[11px] text-zinc-400 mt-0.5">开启后仅允许展开一个菜单</span>
              </div>
              <t-switch v-model="webpanelStore.settings.webPanelDefaultExpandMutex" size="large" />
            </div>
          </div>

          <!-- 提交操作 -->
          <div class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60">
            <t-button
              theme="primary"
              type="submit"
              :loading="webpanelStore.submitLoading"
              class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow"
            >
              保存默认主题设置
            </t-button>
          </div>
        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

:deep(.t-color-container) {
  @apply !p-0 !border-none;
}

:deep(.custom-color-picker) {
  @apply !bg-white dark:!bg-zinc-800 !border !border-zinc-200/50 dark:!border-zinc-700/50 !shadow-2xl !rounded-2xl;
}
:deep(.custom-color-picker .t-color-picker__panel) {
  @apply !bg-transparent;
}

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
