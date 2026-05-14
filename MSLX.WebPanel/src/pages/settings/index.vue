<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useWebpanelStore, useUserStore } from '@/store';

import UserProfileCard from './components/UserProfileCard.vue';
import SystemSettingsCard from './components/SystemSettingsCard.vue';
import WebPanelStyleCard from './components/WebPanelStyleCard.vue';
import PluginSlot from '@/components/PluginSlot.vue';

const webpanelStore = useWebpanelStore();
const userStore = useUserStore();

// 组件引用
const userCardRef = ref();
const sysCardRef = ref();

const initAllData = async () => {
  // 并行加载所有数据
  const promises = [];
  if (userCardRef.value) promises.push(userCardRef.value.initData());
  if (sysCardRef.value) promises.push(sysCardRef.value.initData());
  promises.push(webpanelStore.fetchSettings());

  await Promise.all(promises);
};

onMounted(() => {
  initAllData();
});
</script>

<template>
  <div class="settings-page">
    <t-space direction="vertical" size="large" style="width: 100%">
      <user-profile-card ref="userCardRef" />

      <system-settings-card v-if="userStore.isAdmin" ref="sysCardRef" @refresh="initAllData" />

      <web-panel-style-card v-if="userStore.isAdmin" />

      <!--插件扩展区域 "settings-profile-bottom -->
      <plugin-slot name="settings-profile-bottom" />
    </t-space>
  </div>
</template>

<style scoped lang="less">
.settings-page {
  width: 100%;
}
</style>
