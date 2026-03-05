<script setup lang="ts">
import { ref } from 'vue';
import QuickMode from './components/QuickMode.vue';
import CustomMode from './components/CustomMode.vue';
import PackageMode from './components/PackageMode.vue'

const value = ref(1);
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-zinc-800 dark:text-zinc-200 pb-5">

    <div
      class="design-card list-item-anim flex flex-col sm:flex-row sm:items-center justify-between gap-5 p-5 sm:p-6 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left"
      style="animation-delay: 0s;"
    >
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none tracking-tight">创建服务端实例</h2>
          <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">选择适合您的部署模式，快速建立或深度定制您的 Minecraft 服务器</span>
        </div>
      </div>

      <div class="flex items-center shrink-0">
        <t-radio-group v-model="value" variant="default-filled">
          <t-radio-button :value="1">快速模式</t-radio-button>
          <t-radio-button :value="2">上传整合包</t-radio-button>
          <t-radio-button :value="3">自定义模式</t-radio-button>
        </t-radio-group>
      </div>
    </div>

    <div class="relative w-full">
      <div v-show="value === 1" class="list-item-anim" style="animation-delay: 0.1s;">
        <quick-mode />
      </div>

      <div v-show="value === 2" class="list-item-anim" style="animation-delay: 0.1s;">
        <package-mode />
      </div>

      <div v-show="value === 3" class="list-item-anim" style="animation-delay: 0.1s;">
        <custom-mode />
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
