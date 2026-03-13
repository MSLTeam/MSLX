<script setup lang="ts">
import { computed } from 'vue';
import { useSettingStore } from '@/store';
import { changeUrl } from '@/router';

const settingStore = useSettingStore();

</script>

<template>
  <div class="relative flex items-center justify-center w-screen min-h-screen transition-all duration-300 bg-cover bg-center bg-no-repeat page-bg before:absolute before:inset-0 before:bg-white/20 dark:before:bg-black/40 before:transition-colors">

    <div class="relative z-10 flex flex-col items-center text-center w-[420px] max-w-[90%] p-10 md:p-12 mx-5 bg-white/60 dark:bg-[#1e1e28]/50 backdrop-blur-xl border border-white/40 dark:border-white/15 rounded-2xl shadow-[0_8px_32px_0_rgba(0,0,0,0.2)] transition-all duration-300">

      <div class="mb-10 w-full">
        <h1 class="text-[72px] md:text-[96px] font-bold leading-tight m-0 bg-gradient-to-br from-[#40a9ff] to-[#1890ff] dark:from-[#69c0ff] dark:to-[#40a9ff] bg-clip-text text-transparent opacity-90 select-none">
          404
        </h1>

        <h2 class="text-2xl font-semibold mt-2.5 !mb-4 tracking-wide text-zinc-800 dark:text-white">
          掉出了这个世界......
        </h2>
        <p class="text-sm leading-relaxed !px-5 m-0 text-zinc-600 dark:text-zinc-300/80">
          越过基岩的边界，只剩无尽的黑夜，<br>请点击重生，寻回原本的世界。
        </p>
      </div>

      <div class="w-full">
        <t-button
          theme="primary"
          size="large"
          block
          class="!rounded-xl !h-12 !text-base shadow-lg shadow-[var(--color-primary)]/20 hover:shadow-[var(--color-primary)]/40 transition-shadow"
          @click="changeUrl('/')"
        >
          返回主页
        </t-button>
      </div>

      <footer class="mt-8 text-xs text-[var(--td-text-color-secondary)]/60 font-mono tracking-wider">
        Copyright @ 2021-{{ new Date().getFullYear() }} MSLTeam
      </footer>

    </div>
  </div>
</template>

<style lang="less" scoped>
@reference "@/style/tailwind/index.css";

.page-bg {
  background-image: url('@/assets/bg_light_new.jpg');
}

:global(html[theme-mode='dark']) .page-bg,
:global(html.dark) .page-bg {
  background-image: url('@/assets/bg_night_new.jpg');
}
</style>
