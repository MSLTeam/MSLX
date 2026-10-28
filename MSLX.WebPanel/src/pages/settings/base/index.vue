<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';
const targetNodeId = getPageNodeId();
import { useWebpanelStore, useUserStore } from '@/store';

import type { UserInfoModel } from '@/api/model/user';
import AccountSecuritySettings from './components/AccountSecuritySettings.vue';
const accountInfo = ref<UserInfoModel>();

import UserProfileCard from './components/UserProfileCard.vue';
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
  if (targetNodeId === 'local') promises.push(webpanelStore.fetchSettings());

  await Promise.all(promises);
};

onMounted(() => {
  initAllData();
});
</script>

<template>
  <div class="settings-page">
    <t-space direction="vertical" size="large" style="width: 100%">
      <user-profile-card v-if="targetNodeId === 'local'" ref="userCardRef" @loaded="accountInfo = $event" />
      <account-security-settings
        v-if="targetNodeId === 'local' && accountInfo"
        :security="accountInfo.security"
        :user-id="accountInfo.id"
        @refresh="userCardRef?.initData()"
      />

      <web-panel-style-card v-if="userStore.isAdmin && targetNodeId === 'local'" />

      <t-alert
        v-if="targetNodeId !== 'local'"
        theme="info"
        message="此处显示所选子节点的插件设置。登录账号资料和当前面板外观由主节点统一管理。"
      />
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
