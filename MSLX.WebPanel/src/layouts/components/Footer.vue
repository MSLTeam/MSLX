<template>
  <footer class="w-full flex justify-center items-center">
    <div class="glass-capsule design-card group flex items-center gap-3 px-6 py-2 rounded-full bg-[var(--td-bg-color-container)]/80 backdrop-blur-[10px] border border-zinc-200/80 dark:border-zinc-700/60 shadow-sm transition-all duration-300 font-mono text-[13px] leading-none text-[var(--td-text-color-secondary)] cursor-default">

      <span class="font-bold text-[var(--td-text-color-primary)] tracking-[0.5px] transition-colors duration-300 group-hover:text-[var(--color-primary)]">
        MSLX-WEBPANEL
      </span>

      <span class="font-light text-zinc-500 dark:text-zinc-600">/</span>

      <span class="font-bold text-[var(--td-text-color-primary)] tracking-[0.5px] transition-colors duration-300 group-hover:text-[var(--color-primary)]">
        MSLTEAM
      </span>

      <span class="font-light text-zinc-500 dark:text-zinc-600">/</span>

      <span class="font-bold text-[var(--td-text-color-primary)] tracking-[0.5px] transition-colors duration-300 group-hover:text-[var(--color-primary)]">
        © 2021-{{ new Date().getFullYear() }}
      </span>

    </div>
  </footer>
</template>

<script setup lang="ts">
// 彻底干掉前缀引入，完全拥抱 Tailwind 原子类
</script>

<style scoped>
@reference "@/style/tailwind/index.css";

/* 极致纯净的 Style 块：
  布局、排版、基础颜色、边距已经全部在 Template 中用 Tailwind 原子类解决。
  这里仅保留 Tailwind 任意值写法会非常丑陋的复杂多重光影混色逻辑。
*/

.glass-capsule:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--color-primary), transparent 50%);

  /* 柔和的主题色外发光 + 微弱内发光 */
  box-shadow:
    0 6px 20px rgba(0, 0, 0, 0.08),
    0 0 15px color-mix(in srgb, var(--color-primary), transparent 85%),
    inset 0 0 8px color-mix(in srgb, var(--color-primary), transparent 90%);
}

/* 暗黑模式光影特调 */
:global(html[theme-mode='dark']) .glass-capsule:hover {
  border-color: color-mix(in srgb, var(--color-primary), transparent 60%);

  box-shadow:
    0 6px 20px rgba(0, 0, 0, 0.4),
    0 0 20px color-mix(in srgb, var(--color-primary), transparent 80%),
    inset 0 0 8px color-mix(in srgb, var(--color-primary), transparent 85%);
}
</style>
