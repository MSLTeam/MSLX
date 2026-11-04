const test = require('node:test');
const assert = require('node:assert/strict');

const STYLE_CONFIG = {
  showFooter: true,
  isSidebarCompact: false,
  showBreadcrumb: false,
  mode: 'auto',
  layout: 'side',
  splitMenu: false,
  isFooterAside: false,
  isSidebarFixed: true,
  isHeaderFixed: true,
  showHeader: true,
  backgroundTheme: 'blueGrey',
  brandTheme: 'default',
  enableCustomTheme: false,
  expandMutex: true,
};

function detectIsThemeCustomized(rawSettingJson) {
  if (!rawSettingJson) return false;
  try {
    const parsed = typeof rawSettingJson === 'string' ? JSON.parse(rawSettingJson) : rawSettingJson;
    if (typeof parsed.isThemeCustomized === 'boolean') {
      return parsed.isThemeCustomized;
    }
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
}

function createSettingStore(initialRawStorage = null) {
  const isCustomized = detectIsThemeCustomized(initialRawStorage);
  let parsedStorage = {};
  if (initialRawStorage) {
    try {
      parsedStorage = JSON.parse(initialRawStorage);
    } catch {}
  }

  const state = {
    ...STYLE_CONFIG,
    ...parsedStorage,
    isThemeCustomized: isCustomized,
  };

  return {
    state,
    setThemeCustomized(val) {
      state.isThemeCustomized = val;
    },
    applyDefaultTheme(defaults) {
      if (state.isThemeCustomized) return;
      if (defaults.webPanelDefaultThemeMode !== undefined) {
        state.mode = defaults.webPanelDefaultThemeMode;
      }
      if (defaults.webPanelDefaultBrandTheme !== undefined) {
        state.brandTheme = defaults.webPanelDefaultBrandTheme;
      }
      if (defaults.webPanelDefaultLayout !== undefined) {
        state.layout = defaults.webPanelDefaultLayout;
      }
      if (defaults.webPanelDefaultEnableCustomTheme !== undefined) {
        state.enableCustomTheme = defaults.webPanelDefaultEnableCustomTheme;
      }
      if (defaults.webPanelDefaultExpandMutex !== undefined) {
        state.expandMutex = defaults.webPanelDefaultExpandMutex;
      }
    },
    resetToDefault(serverDefaults) {
      state.isThemeCustomized = false;
      if (serverDefaults) {
        this.applyDefaultTheme(serverDefaults);
      } else {
        state.mode = STYLE_CONFIG.mode;
        state.brandTheme = STYLE_CONFIG.brandTheme;
        state.layout = STYLE_CONFIG.layout;
        state.enableCustomTheme = STYLE_CONFIG.enableCustomTheme;
        state.expandMutex = STYLE_CONFIG.expandMutex;
      }
    },
  };
}

test('first-time visitor with no local storage adopts server default theme', () => {
  const store = createSettingStore(null);
  assert.equal(store.state.isThemeCustomized, false);

  const serverDefaults = {
    webPanelDefaultThemeMode: 'dark',
    webPanelDefaultBrandTheme: 'purple',
    webPanelDefaultLayout: 'top',
    webPanelDefaultEnableCustomTheme: true,
    webPanelDefaultExpandMutex: false,
  };

  store.applyDefaultTheme(serverDefaults);

  assert.equal(store.state.mode, 'dark');
  assert.equal(store.state.brandTheme, 'purple');
  assert.equal(store.state.layout, 'top');
  assert.equal(store.state.enableCustomTheme, true);
  assert.equal(store.state.expandMutex, false);
  assert.equal(store.state.isThemeCustomized, false);
});

test('visitor who customized theme preserves local settings and rejects server defaults', () => {
  const store = createSettingStore(null);
  // User customizes theme to pink
  store.state.brandTheme = 'pink';
  store.state.mode = 'light';
  store.setThemeCustomized(true);

  assert.equal(store.state.isThemeCustomized, true);

  const serverDefaults = {
    webPanelDefaultThemeMode: 'dark',
    webPanelDefaultBrandTheme: 'green',
    webPanelDefaultLayout: 'top',
    webPanelDefaultEnableCustomTheme: true,
  };

  store.applyDefaultTheme(serverDefaults);

  assert.equal(store.state.brandTheme, 'pink');
  assert.equal(store.state.mode, 'light');
  assert.equal(store.state.layout, 'side');
  assert.equal(store.state.enableCustomTheme, false);
});

test('visitor who never customized theme dynamically receives updated server defaults', () => {
  const store = createSettingStore(null);
  store.applyDefaultTheme({ webPanelDefaultBrandTheme: 'cyan' });
  assert.equal(store.state.brandTheme, 'cyan');

  // Admin later changes default to orange
  store.applyDefaultTheme({ webPanelDefaultBrandTheme: 'orange' });
  assert.equal(store.state.brandTheme, 'orange');
});

test('resetToDefault resets customization flag and reapplies server defaults', () => {
  const store = createSettingStore(null);
  store.state.brandTheme = 'red';
  store.setThemeCustomized(true);

  assert.equal(store.state.isThemeCustomized, true);

  const serverDefaults = {
    webPanelDefaultBrandTheme: 'cyan',
    webPanelDefaultThemeMode: 'dark',
  };

  store.resetToDefault(serverDefaults);

  assert.equal(store.state.isThemeCustomized, false);
  assert.equal(store.state.brandTheme, 'cyan');
  assert.equal(store.state.mode, 'dark');
});

test('migrates existing visitor: custom values recognized as customized, defaults as non-customized', () => {
  const customizedRaw = JSON.stringify({ brandTheme: 'yellow', mode: 'auto' });
  assert.equal(detectIsThemeCustomized(customizedRaw), true);

  const defaultRaw = JSON.stringify({ brandTheme: 'default', mode: 'auto', layout: 'side', enableCustomTheme: false, expandMutex: true });
  assert.equal(detectIsThemeCustomized(defaultRaw), false);
});
