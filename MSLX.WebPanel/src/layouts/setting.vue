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
    <div class="setting-container">
      <t-form ref="form" :data="formData" label-align="left">
        <div class="setting-group-title">主题模式</div>
        <t-radio-group v-model="formData.mode">
          <div v-for="(item, index) in MODE_OPTIONS" :key="index" class="setting-layout-drawer">
            <div>
              <t-radio-button :key="index" :value="item.type">
                <component :is="getModeIcon(item.type)" />
              </t-radio-button>
              <p :style="{ textAlign: 'center', marginTop: '8px' }">{{ item.text }}</p>
            </div>
          </div>
        </t-radio-group>

        <div class="setting-group-title">个性化</div>
        <div class="setting-item-row">
          <span class="setting-item-label">开启背景美化</span>
          <t-switch v-model="formData.enableCustomTheme" />
        </div>

        <div class="setting-group-title">主题色</div>
        <t-radio-group v-model="formData.brandTheme">
          <div
            v-for="(item, index) in COLOR_OPTIONS.slice(0, COLOR_OPTIONS.length - 1)"
            :key="index"
            class="setting-layout-drawer"
          >
            <t-radio-button :key="index" :value="item" class="setting-layout-color-group">
              <color-container :value="item" />
            </t-radio-button>
          </div>
          <div class="setting-layout-drawer">
            <t-popup
              destroy-on-close
              expand-animation
              placement="bottom-right"
              trigger="click"
              :visible="isColoPickerDisplay"
              :overlay-style="{ padding: 0 }"
              @visible-change="onPopupVisibleChange"
            >
              <template #content>
                <t-color-picker-panel
                  :on-change="changeColor"
                  :color-modes="['monochrome']"
                  format="HEX"
                  :swatch-colors="[]"
                />
              </template>
              <t-radio-button
                :value="COLOR_OPTIONS[COLOR_OPTIONS.length - 1]"
                class="setting-layout-color-group dynamic-color-btn"
              >
                <color-container :value="COLOR_OPTIONS[COLOR_OPTIONS.length - 1]" />
              </t-radio-button>
            </t-popup>
          </div>
        </t-radio-group>

        <div class="setting-group-title">导航布局</div>
        <t-radio-group v-model="formData.layout">
          <div v-for="(item, index) in LAYOUT_OPTION" :key="index" class="setting-layout-drawer">
            <t-radio-button :key="index" :value="item">
              <thumbnail :src="getThumbnailUrl(item)" />
            </t-radio-button>
          </div>
        </t-radio-group>
      </t-form>
    </div>
  </t-drawer>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watchEffect, onBeforeUnmount } from 'vue';
import type { PopupVisibleChangeContext } from 'tdesign-vue-next';
import { Color } from 'tvision-color';

import { useSettingStore } from '@/store';
import Thumbnail from '@/components/thumbnail/index.vue';
import ColorContainer from '@/components/color/index.vue';

import STYLE_CONFIG from '@/config/style';
import { insertThemeStylesheet, generateColorMap } from '@/config/color';

import SettingDarkIcon from '@/assets/assets-setting-dark.svg';
import SettingLightIcon from '@/assets/assets-setting-light.svg';
import SettingAutoIcon from '@/assets/assets-setting-auto.svg';

const settingStore = useSettingStore();

const screenWidth = ref(window.innerWidth);
const isMobile = computed(() => screenWidth.value < 480);
const drawerSize = computed(() => {
  return isMobile.value ? '85%' : '408px';
});

const updateScreenWidth = () => {
  screenWidth.value = window.innerWidth;
};

const LAYOUT_OPTION = ['side', 'top'];
const COLOR_OPTIONS = ['default', 'cyan', 'green', 'yellow', 'orange', 'red', 'pink', 'purple', 'dynamic'];
const MODE_OPTIONS = [
  { type: 'auto', text: '跟随系统' },
  { type: 'light', text: '明亮' },
  { type: 'dark', text: '暗黑' },
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

// 初始化表单数据
const formData = ref({ ...initStyleConfig() });
if (isMobile.value && formData.value.layout === 'side') {
  formData.value.layout = 'top';
}
const isColoPickerDisplay = ref(false);

const showSettingPanel = computed({
  get() {
    return settingStore.showSettingPanel;
  },
  set(newVal: boolean) {
    settingStore.updateConfig({
      showSettingPanel: newVal,
    });
  },
});

const changeColor = (hex: string) => {
  const newPalette = Color.getPaletteByGradation({
    colors: [hex],
    step: 10,
  })[0];
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

const getModeIcon = (mode: string) => {
  if (mode === 'light') {
    return SettingLightIcon;
  }
  if (mode === 'dark') {
    return SettingDarkIcon;
  }
  return SettingAutoIcon;
};

const handleCloseDrawer = () => {
  settingStore.updateConfig({
    showSettingPanel: false,
  });
};

const getThumbnailUrl = (name: string): string => {
  return `https://tdesign.gtimg.com/tdesign-pro/setting/${name}.png`;
};

// 监听 formData 变化并同步到 Store
watchEffect(() => {
  settingStore.updateConfig(formData.value);
});
</script>

<style lang="less" scoped>
/* 开关行 */
.setting-item-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 8px;

  .setting-item-label {
    font-size: 14px;
    color: var(--td-text-color-primary);
  }
}

.tdesign-setting {
  z-index: 100;
  position: fixed;
  bottom: 200px;
  right: 0;
  height: 40px;
  width: 40px;
  border-radius: 20px 0 0 20px;
  transition: all 0.3s;

  .t-icon {
    margin-left: 8px;
  }

  .tdesign-setting-text {
    font-size: 12px;
    display: none;
  }

  &:hover {
    width: 96px;

    .tdesign-setting-text {
      display: inline-block;
    }
  }
}

.setting-layout-color-group {
  display: inline-flex;
  justify-content: center;
  align-items: center;
  border-radius: 50% !important;
  padding: 6px !important;
  border: 2px solid transparent !important;

  > .t-radio-button__label {
    display: inline-flex;
  }
}

.tdesign-setting-close {
  position: fixed;
  bottom: 200px;
  right: 300px;
}

.setting-group-title {
  font-size: 14px;
  line-height: 22px;
  margin: 32px 0 24px 0;
  text-align: left;
  font-family: PingFang SC;
  font-style: normal;
  font-weight: 500;
  color: var(--td-text-color-primary);
}

.setting-link {
  cursor: pointer;
  color: var(--td-brand-color);
  margin-bottom: 8px;
}

.setting-info {
  position: absolute;
  padding: 24px;
  bottom: 0;
  left: 0;
  line-height: 20px;
  font-size: 12px;
  text-align: center;
  color: var(--td-text-color-placeholder);
  width: 100%;
  background: var(--td-bg-color-container);
}

.setting-drawer-container {
  .setting-container {
    padding-bottom: 100px;

    /* 移动端增加左右内边距 */
    @media (max-width: 480px) {
      padding: 0 16px 100px 16px;
    }
  }

  :deep(.t-radio-group.t-size-m) {
    min-height: 32px;
    width: 100%;
    justify-content: space-between;
    align-items: center;
    flex-wrap: wrap;
    gap: 16px;

    /* 移动端靠左排列 */
    @media (max-width: 480px) {
      justify-content: flex-start;
    }
  }

  :deep(.t-radio-group.t-size-m .t-radio-button) {
    height: auto;
  }

  .setting-layout-drawer {
    display: flex;
    flex-direction: column;
    align-items: center;

    :deep(.t-radio-button) {
      display: inline-flex;
      max-height: 78px;
      padding: 8px;
      border-radius: var(--td-radius-default);
      border: 2px solid #e3e6eb;
      > .t-radio-button__label {
        display: inline-flex;
      }
    }

    :deep(.t-is-checked) {
      border: 2px solid var(--td-brand-color) !important;
    }

    :deep(.t-form__controls-content) {
      justify-content: end;
    }
  }

  :deep(.t-form__controls-content) {
    justify-content: end;
  }
}

.setting-route-theme {
  :deep(.t-form__label) {
    min-width: 310px !important;
    color: var(--td-text-color-secondary);
  }
}

.setting-color-theme {
  .setting-layout-drawer {
    :deep(.t-radio-button) {
      height: 32px;
    }

    &:last-child {
      margin-right: 0;
    }
  }
}
</style>
