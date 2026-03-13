<script setup lang="ts">
import { ref } from 'vue';
import Custom from '@/pages/frp/createFrp/components/Custom.vue';
import Index from '@/pages/frp/createFrp/components/MSLFrp/index.vue';
import MSLP2P from '@/pages/frp/createFrp/components/MSLP2P.vue';
import MEFrp from '@/pages/frp/createFrp/components/MEFrp/index.vue';

const value = ref(1);
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">

    <div
      class="design-card list-item-anim flex flex-col sm:flex-row sm:items-center justify-between gap-5 p-5 sm:p-6 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left"
      style="animation-delay: 0s;"
    >
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">创建穿透隧道</h2>
          <span class="text-xs text-[var(--td-text-color-secondary)] mt-1.5 font-medium">选择适合您的内网穿透服务，快速将本地服务器暴露至公网以进行联机···</span>
        </div>
      </div>

      <div class="flex items-center shrink-0">
        <t-radio-group v-model="value" variant="default-filled">
          <t-radio-button :value="1">MSLFrp</t-radio-button>
          <t-radio-button :value="2">MSL 联机</t-radio-button>
          <t-radio-button :value="3">ME Frp</t-radio-button>
          <t-radio-button :value="888">自定义配置</t-radio-button>
        </t-radio-group>
      </div>
    </div>

    <div class="relative w-full">
      <div v-show="value === 1" class="list-item-anim" style="animation-delay: 0.1s;">
        <index />
      </div>

      <div v-show="value === 2" class="list-item-anim" style="animation-delay: 0.1s;">
        <m-s-l-p2-p />
      </div>

      <div v-show="value === 3" class="list-item-anim" style="animation-delay: 0.1s;">
        <m-e-frp />
      </div>

      <div v-show="value === 888" class="list-item-anim" style="animation-delay: 0.1s;">
        <custom />
      </div>
    </div>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 首次渲染阶梯滑入动画 */
.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
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
