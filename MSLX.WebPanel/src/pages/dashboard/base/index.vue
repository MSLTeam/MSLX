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
  <div class="flex flex-col gap-6 mx-auto w-full min-h-screen pb-6">

    <t-alert
      v-if="isDefaultUser"
      theme="warning"
      title="安全风险提示"
      message="检测到您当前正在使用默认账号 (mslx)。为了保障系统安全，请务必尽快修改用户名和密码！"
      class="list-item-anim w-full shadow-sm"
      style="animation-delay: 0s;"
    >
      <template #operation>
        <span class="cursor-pointer font-bold flex items-center gap-1 hover:opacity-80 transition-opacity" @click="changeUrl('/settings')">
          去修改 <i class="fa-solid fa-arrow-right text-sm"></i>
        </span>
      </template>
    </t-alert>

    <banner class="list-item-anim" :style="{ animationDelay: isDefaultUser ? '0.05s' : '0s' }" />
    <info-card class="list-item-anim" :style="{ animationDelay: isDefaultUser ? '0.1s' : '0.05s' }" />
    <system-status class="list-item-anim" :style="{ animationDelay: isDefaultUser ? '0.15s' : '0.1s' }" />
    <announcement class="list-item-anim" :style="{ animationDelay: isDefaultUser ? '0.2s' : '0.15s' }" />

  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
