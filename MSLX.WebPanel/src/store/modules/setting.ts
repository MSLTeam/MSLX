import { defineStore } from 'pinia';
import keys from 'lodash/keys';
import {
  COLOR_TOKEN,
  LIGHT_CHART_COLORS,
  DARK_CHART_COLORS,
  TColorSeries,
  applyBrandThemeStyle,
} from '@/config/color';
import STYLE_CONFIG from '@/config/style';
import { store } from '@/store';

// 检查访客本地是否曾自定义过主题样式
const getInitialCustomizedStatus = (): boolean => {
  const persistedRaw = localStorage.getItem('setting');
  if (!persistedRaw) return false;
  try {
    const parsed = JSON.parse(persistedRaw);
    if (typeof parsed.isThemeCustomized === 'boolean') {
      return parsed.isThemeCustomized;
    }
    // 旧版本未记录该字段时，比对是否偏离初始默认配置
    return (
      (parsed.mode && parsed.mode !== STYLE_CONFIG.mode) ||
      (parsed.brandTheme && parsed.brandTheme !== STYLE_CONFIG.brandTheme) ||
      (parsed.layout && parsed.layout !== STYLE_CONFIG.layout) ||
      (parsed.enableCustomTheme !== undefined && parsed.enableCustomTheme !== STYLE_CONFIG.enableCustomTheme) ||
      (parsed.expandMutex !== undefined && parsed.expandMutex !== STYLE_CONFIG.expandMutex)
    );
  } catch {
    return false;
  }
};

const state = {
  ...STYLE_CONFIG,
  isThemeCustomized: getInitialCustomizedStatus(),
  isMobileLayout: window.innerWidth < 480,
  showSettingPanel: false,
  colorList: COLOR_TOKEN,
  chartColors: LIGHT_CHART_COLORS,
  systemTheme: window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light',
};

export type TState = typeof state;

export const useSettingStore = defineStore('setting', {
  state: () => state,
  getters: {
    effectiveLayout: (state) => state.isMobileLayout && !state.isThemeCustomized ? 'top' : state.layout,
    showSidebar(): boolean { return this.effectiveLayout !== 'top'; },
    showSidebarLogo(): boolean { return this.effectiveLayout === 'side'; },
    showHeaderLogo(): boolean { return this.effectiveLayout !== 'side'; },
    displayMode: (state): 'dark' | 'light' => {
      if (state.mode === 'auto') {
        return state.systemTheme as 'dark' | 'light';
      }
      return state.mode as 'dark' | 'light';
    },
  },
  actions: {
    async changeMode(mode: 'dark' | 'light' | 'auto') {
      let theme = mode;

      if (mode === 'auto') {
        theme = this.systemTheme as 'dark' | 'light';
      }
      const isDarkMode = theme === 'dark';

      document.documentElement.setAttribute('theme-mode', isDarkMode ? 'dark' : '');

      this.chartColors = isDarkMode ? DARK_CHART_COLORS : LIGHT_CHART_COLORS;
      this.changeBrandTheme(this.brandTheme);
    },
    setSystemTheme(theme: 'dark' | 'light') {
      this.systemTheme = theme;
      if (this.mode === 'auto') {
        this.changeMode('auto');
      }
    },
    changeBrandTheme(brandTheme: string) {
      applyBrandThemeStyle(brandTheme, this.displayMode, (color) => this.addColor(color));
    },
    addColor(payload: TColorSeries) {
      this.colorList = { ...this.colorList, ...payload };
    },
    setThemeCustomized(val: boolean) {
      if (val && !this.isThemeCustomized) this.layout = this.effectiveLayout;
      this.isThemeCustomized = val;
    },
    applyDefaultTheme(defaults: {
      webPanelDefaultThemeMode?: string;
      webPanelDefaultBrandTheme?: string;
      webPanelDefaultLayout?: string;
      webPanelDefaultEnableCustomTheme?: boolean;
      webPanelDefaultExpandMutex?: boolean;
    }) {
      if (this.isThemeCustomized) return;
      const newConfig: Record<string, any> = {};
      if (defaults.webPanelDefaultThemeMode !== undefined) {
        newConfig.mode = defaults.webPanelDefaultThemeMode;
      }
      if (defaults.webPanelDefaultBrandTheme !== undefined) {
        newConfig.brandTheme = defaults.webPanelDefaultBrandTheme;
      }
      if (defaults.webPanelDefaultLayout !== undefined) {
        newConfig.layout = defaults.webPanelDefaultLayout;
      }
      if (defaults.webPanelDefaultEnableCustomTheme !== undefined) {
        newConfig.enableCustomTheme = defaults.webPanelDefaultEnableCustomTheme;
      }
      if (defaults.webPanelDefaultExpandMutex !== undefined) {
        newConfig.expandMutex = defaults.webPanelDefaultExpandMutex;
      }
      if (Object.keys(newConfig).length > 0) {
        this.updateConfig(newConfig);
      }
    },
    resetToDefault(defaultSettings?: {
      webPanelDefaultThemeMode?: string;
      webPanelDefaultBrandTheme?: string;
      webPanelDefaultLayout?: string;
      webPanelDefaultEnableCustomTheme?: boolean;
      webPanelDefaultExpandMutex?: boolean;
    }) {
      this.isThemeCustomized = false;
      if (defaultSettings) {
        this.applyDefaultTheme(defaultSettings);
      } else {
        this.updateConfig({
          mode: STYLE_CONFIG.mode,
          layout: STYLE_CONFIG.layout,
          brandTheme: STYLE_CONFIG.brandTheme,
          enableCustomTheme: STYLE_CONFIG.enableCustomTheme,
          expandMutex: STYLE_CONFIG.expandMutex,
        });
      }
    },
    updateConfig(payload: Partial<TState>) {
      for (const key in payload) {
        if (payload[key] !== undefined) {
          this[key] = payload[key];
        }
        if (key === 'mode') {
          this.changeMode(payload[key]);
        }
        if (key === 'brandTheme') {
          this.changeBrandTheme(payload[key]);
        }
      }
    },
  },
  persist: {
    paths: [...keys(STYLE_CONFIG), 'colorList', 'chartColors', 'isThemeCustomized'],
  },
});

export function getSettingStore() {
  return useSettingStore(store);
}
