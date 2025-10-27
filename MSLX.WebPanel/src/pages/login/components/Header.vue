<template>
  <header class="login-header">
    <span class="logo">
      <img src="@/assets/logo.png" :class="`${prefix}-side-nav-logo-img`" alt="logo" />
      <span> MSLX 管理中心 </span>
    </span>
    <div class="operations-container">
      <t-button theme="default" shape="square" variant="text" @click="navToGitHub">
        <t-icon name="logo-github" class="icon" />
      </t-button>
      <t-button theme="default" shape="square" variant="text" @click="navToHelper">
        <t-icon name="help-circle" class="icon" />
      </t-button>
      <t-button theme="default" shape="square" variant="text" @click="toggleSettingPanel">
        <t-icon name="setting" class="icon" />
      </t-button>
    </div>
  </header>
</template>

<script setup lang="ts">
import { useSettingStore } from '@/store';
import { prefix } from '@/config/global';

const settingStore = useSettingStore();
const toggleSettingPanel = () => {
  settingStore.updateConfig({
    showSettingPanel: true,
  });
};

const navToGitHub = () => {
  window.open('https://github.com/MSLTeam/MSLX');
};

const navToHelper = () => {
  window.open('https://www.mslmc.cn');
};
</script>

<style lang="less" scoped>
.login-header {
  padding: 12px 24px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  backdrop-filter: blur(5px);
  color: var(--td-text-color-primary);

  .logo {
    display: flex;
    align-items: center;

    img {
      width: 32px;
      margin-right: 8px;
    }

    span {
      font-size: 18px;
      font-weight: bold;
      white-space: nowrap;
    }
  }

  .operations-container {
    display: flex;
    align-items: center;
    .t-button {
      margin-left: 16px;
    }

    .icon {
      height: 20px;
      width: 20px;
      padding: 6px;
      box-sizing: content-box;

      &:hover {
        cursor: pointer;
      }
    }
  }
}
</style>
