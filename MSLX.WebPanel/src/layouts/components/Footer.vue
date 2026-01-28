<template>
  <footer :class="`${prefix}-footer-centered-lite`">
    <div class="glass-capsule design-card">
      <span class="brand">MSLX-Webpanel</span>
      <span class="divider">/</span>
      <span class="brand">MSLTEAM</span>
      <span class="divider">/</span>
      <span class="copyright">© 2021-{{ new Date().getFullYear() }}</span>
    </div>
  </footer>
</template>

<script setup lang="ts">
import { prefix } from '@/config/global';
</script>

<style lang="less" scoped>
.@{starter-prefix}-footer-centered-lite {
  width: 100%;
  display: flex;
  justify-content: center;
  align-items: center;

  .glass-capsule {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 24px;
    background: var(--td-bg-color-container);
    backdrop-filter: blur(10px);
    border: 1px solid var(--td-component-border);
    border-radius: 32px;
    box-shadow: var(--td-shadow-1);
    font-family: 'SF Mono', 'Roboto Mono', Consolas, monospace;
    font-size: 13px;
    line-height: 1;
    color: var(--td-text-color-secondary);
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    cursor: default;

    .brand {
      font-weight: 700;
      color: var(--td-text-color-primary);
      letter-spacing: 0.5px;
      transition: color 0.3s;
    }

    .copyright {
      font-weight: 500;
      opacity: 0.8;
    }
    &:hover {
      transform: translateY(-2px);
      border-color: color-mix(in srgb, var(--td-brand-color), transparent 50%);

      box-shadow:
        0 6px 20px rgba(0, 0, 0, 0.08),
        0 0 15px color-mix(in srgb, var(--td-brand-color), transparent 85%),
        inset 0 0 8px color-mix(in srgb, var(--td-brand-color), transparent 90%); // 微弱内发光

      .brand {
        color: var(--td-brand-color);
      }
    }
  }
}

// 暗黑模式适配
:deep([theme-mode='dark']) .@{starter-prefix}-footer-centered-lite .glass-capsule {
  border-color: rgba(255, 255, 255, 0.08);

  &:hover {
    border-color: color-mix(in srgb, var(--td-brand-color), transparent 60%);
    box-shadow:
      0 6px 20px rgba(0, 0, 0, 0.4),
      0 0 20px color-mix(in srgb, var(--td-brand-color), transparent 80%);
  }
}
</style>
