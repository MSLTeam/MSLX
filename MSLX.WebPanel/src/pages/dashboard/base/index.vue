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
  <div class="flex flex-col gap-5 mx-auto w-full min-h-screen">

    <t-alert
      v-if="isDefaultUser"
      theme="warning"
      title="安全风险提示"
      message="检测到您当前正在使用默认账号 (mslx)。为了保障系统安全，请务必尽快修改用户名和密码！"
      class="w-full shadow-sm transition-all"
    >
      <template #operation>
        <span class="cursor-pointer font-bold flex items-center gap-1 hover:opacity-80 transition-opacity" @click="changeUrl('/settings')">
          去修改 <i class="fa-solid fa-arrow-right text-sm"></i>
        </span>
      </template>
    </t-alert>

    <banner />
    <info-card />
    <system-status />
    <announcement />

  </div>
</template>

<style scoped>
</style>
