<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { getPageNodeId } from '@/utils/nodeTarget';
const targetNodeId = getPageNodeId();
import { useWebpanelStore, useUserStore } from '@/store';

import SystemSettingsCard from '../base/components/SystemSettingsCard.vue';
import PluginSlot from '@/components/PluginSlot.vue';
import SslSettingsCard from '../base/components/SslSettingsCard.vue';
import AiSettingsCard from '../base/components/AiSettingsCard.vue';

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
    <t-alert v-if="targetNodeId !== 'local'" theme="warning" class="mb-4" message="正在编辑所选子节点。修改监听地址、端口或 SSL 后需重启，并确认主节点登记的节点地址仍然可用。" />
    <t-space direction="vertical" size="large" style="width: 100%">
      <system-settings-card v-if="userStore.isAdmin" ref="sysCardRef" @refresh="initAllData" />

      <ai-settings-card v-if="userStore.isAdmin" />

      <ssl-settings-card v-if="userStore.isAdmin" />

      <!--插件扩展区域 "settings-daemon-bottom -->
      <plugin-slot name="settings-daemon-bottom" />
    </t-space>
  </div>
</template>

<style scoped lang="less">
.settings-page {
  width: 100%;
}
</style>
