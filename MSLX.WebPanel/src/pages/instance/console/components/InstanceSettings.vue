<script setup lang="ts">
import { ref } from 'vue';
import {
  StoreIcon,
  SettingIcon,
  TimeIcon,
  Setting1Icon
} from 'tdesign-icons-vue-next';

import GeneralSettings from './settingsComponents/GeneralSettings.vue';
import ModsPluginsManager from './settingsComponents/ModsPluginsManager.vue';
import ServerProperties from './settingsComponents/ServerProperties.vue'

const visible = ref(false);
const currentTab = ref(0);

const menuItems = [
  { label: '实例设置', icon: SettingIcon },
  { label: '插件/模组', icon: StoreIcon },
  { label: '服务器属性', icon: Setting1Icon },
  { label: '定时任务', icon: TimeIcon },
];

const open = () => {
  visible.value = true;
};


defineExpose({ open });
</script>

<template>
  <t-dialog
    v-model:visible="visible"
    header="实例配置"
    width="90%"
    top="3vh"
    attach="body"
    :footer="false"
    destroy-on-close
    class="settings-dialog"
  >
    <div class="layout-container">

      <div class="sidebar">
        <div
          v-for="(item, index) in menuItems"
          :key="index"
          class="nav-item"
          :class="{ active: currentTab === index }"
          @click="currentTab = index"
        >
          <component :is="item.icon" class="nav-icon" />
          <span class="nav-text">{{ item.label }}</span>
        </div>
      </div>

      <div class="main-content">



        <div v-if="currentTab === 0" class="panel-wrapper">
          <general-settings />
        </div>

        <div v-if="currentTab === 1" class="panel-wrapper">
          <mods-plugins-manager/>
        </div>

        <div v-if="currentTab === 2" class="panel-wrapper">
          <server-properties :instance-id="21"/>
        </div>

        <div v-if="currentTab === 3" class="panel-wrapper">
          <h3 class="panel-title">任务列表</h3>
          <div class="card-content">任务配置...</div>
        </div>

      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar.less';

:deep(.t-dialog__body) {
  padding: 0 !important;
}

.layout-container {
  height: 72vh;
  display: flex;
  flex-direction: row;
  position: relative;
  overflow: hidden;
  background-color: var(--td-bg-color-secondarycontainer);
}

// 侧边栏
.sidebar {
  width: 160px;
  height: 100%;
  background-color: var(--td-bg-color-secondarycontainer);
  border-right: 1px solid var(--td-component-border);
  display: flex;
  flex-direction: column;
  padding-top: 12px;
  flex-shrink: 0;
  overflow-y: auto;
}

.nav-item {
  height: 50px;
  display: flex;
  align-items: center;
  padding: 0 20px;
  cursor: pointer;
  color: var(--td-text-color-secondary);
  font-size: 14px;
  transition: all 0.2s;
  position: relative;

  .nav-icon {
    font-size: 18px;
    margin-right: 10px;
    flex-shrink: 0;
  }

  .nav-text {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  &:hover:not(.active) {
    background-color: var(--td-bg-color-container-hover);
  }

  &.active {
    background-color: var(--td-bg-color-container);
    color: var(--td-brand-color);
    font-weight: 600;

    &::before {
      content: '';
      position: absolute;
      left: 0;
      top: 50%;
      transform: translateY(-50%);
      width: 4px;
      height: 24px;
      background-color: var(--td-brand-color);
      border-radius: 0 2px 2px 0;
    }
  }
}

// 内容区
.main-content {
  flex: 1;
  background-color: var(--td-bg-color-container);
  height: 100%;
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.panel-wrapper {
  flex: 1;
  padding-left: 32px;
  padding-bottom: 50px;
  overflow-y: auto;
}

.panel-title {
  font-size: 20px;
  font-weight: 600;
  margin-bottom: 24px;
  color: var(--td-text-color-primary);
}


// 移动端适配
@media (max-width: 768px) {

  // 变成上下布局
  .layout-container {
    flex-direction: column;
    height: 75vh;
  }

  // 横向导航
  .sidebar {
    width: 100%;
    height: auto;
    flex-direction: row;
    border-right: none;
    border-bottom: 1px solid var(--td-component-border);
    padding: 0;
  }

  .nav-item {
    flex: 1;
    justify-content: center;
    padding: 12px 0;
    flex-direction: column;
    height: auto;
    font-size: 12px;
    gap: 4px;

    .nav-icon { margin-right: 0; font-size: 20px; }

    &.active {
      background-color: transparent;

      &::before {
        left: 50%;
        top: auto;
        bottom: 0;
        transform: translateX(-50%);
        width: 24px;
        height: 3px;
        border-radius: 2px 2px 0 0;
      }
    }
  }

  .panel-wrapper {
    .scrollbar-mixin();
    padding: 16px;
    padding-bottom: 80px;
  }
}
</style>
