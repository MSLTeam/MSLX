<script setup lang="ts">
import { computed } from 'vue';
import { useUserStore } from '@/store';

import Banner from './components/Banner.vue'
import InfoCard from './components/InfoCard.vue';
import SystemStatus from './components/SystemStatus.vue'
import Announcement from './components/Announcement.vue';
import { changeUrl } from '@/router';

const userStore = useUserStore();

// 判断是否是默认用户
const isDefaultUser = computed(() => {
  return userStore.userInfo?.username === 'mslx';
});

</script>

<template>
  <div>
    <t-alert
      v-if="isDefaultUser"
      theme="warning"
      title="安全风险提示"
      message="检测到您当前正在使用默认账号 (mslx)。为了保障系统安全，请务必尽快修改用户名和密码！"
      style="margin-bottom: 16px;"
    >
      <template #operation>
        <span style="cursor: pointer" @click="changeUrl('/settings')">去修改</span>
      </template>
    </t-alert>

    <banner />
    <info-card style="margin-top: 12px;"/>
    <system-status style="margin-top: 12px;"/>
    <announcement style="margin-top: 12px;"/>
  </div>
</template>

<style scoped lang="less"></style>
