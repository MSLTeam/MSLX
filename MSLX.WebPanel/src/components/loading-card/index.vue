<script lang="ts" setup>
import defaultMascot from '@/assets/logo.png';

interface Props {
  text?: string;
  description?: string;
  minHeight?: string;
  cardClass?: string;
  image?: string;
  imageSize?: string;
}

const props = withDefaults(defineProps<Props>(), {
  text: '正在加载中...',
  description: '请稍候，正在获取最新数据',
  minHeight: '400px',
  cardClass: '',
  image: '',
  imageSize: '68px',
});

const mascotSrc = props.image || defaultMascot;
</script>

<template>
  <div
    class="w-full flex items-center justify-center p-4 transition-all duration-300"
    :style="{ minHeight: props.minHeight }"
  >
    <div
      class="design-card relative flex flex-col items-center justify-center text-center p-8 sm:p-10 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm max-w-sm w-full gap-5 transition-all select-none"
      :class="props.cardClass"
    >
      <!-- 动效区域 -->
      <slot name="icon">
        <div class="relative w-24 h-24 flex flex-col items-center justify-center">
          <!-- 魔法灵力光晕底衬 -->
          <div class="absolute inset-2 rounded-full bg-cyan-400/20 dark:bg-cyan-500/15 blur-xl animate-pulse" />

          <!-- 环绕漂浮闪烁魔法小星光 -->
          <span class="sparkle sparkle-1">✦</span>
          <span class="sparkle sparkle-2">✧</span>
          <span class="sparkle sparkle-3">✦</span>

          <!-- 悬浮图主体 -->
          <div class="allay-fly-wrapper">
            <slot name="image">
              <img
                :src="mascotSrc"
                alt="mascot"
                class="object-contain filter drop-shadow-[0_4px_12px_rgba(56,189,248,0.35)] select-none pointer-events-none"
                :style="{ width: props.imageSize, height: props.imageSize }"
              />
            </slot>
          </div>

          <!-- 地面微影随高度动态缩放 -->
          <div class="allay-shadow" />
        </div>
      </slot>

      <!-- 文案区域 -->
      <div class="flex flex-col gap-1.5 z-10">
        <slot name="text">
          <h4 class="text-base sm:text-lg font-bold text-[var(--td-text-color-primary)] m-0 tracking-tight">
            {{ props.text }}
          </h4>
        </slot>
        <slot name="description">
          <p
            v-if="props.description"
            class="text-xs sm:text-sm text-[var(--td-text-color-secondary)] m-0 leading-relaxed max-w-xs"
          >
            {{ props.description }}
          </p>
        </slot>
      </div>

      <!-- 极光流动微进度条 -->
      <div class="w-40 h-1 bg-[var(--td-component-border)]/60 rounded-full overflow-hidden relative z-10">
        <div class="loading-shimmer-bar" />
      </div>

      <!-- 底部扩展插槽 -->
      <slot name="extra" />
    </div>
  </div>
</template>

<style scoped>
.allay-fly-wrapper {
  animation: allayHover 2.4s ease-in-out infinite;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2;
  transform-origin: center center;
}

@keyframes allayHover {
  0% {
    transform: translateY(2px) scale(0.98) rotate(0deg);
  }
  30% {
    transform: translateY(-8px) scale(1.03) rotate(-2.5deg);
  }
  65% {
    transform: translateY(-4px) scale(1.01) rotate(2deg);
  }
  100% {
    transform: translateY(2px) scale(0.98) rotate(0deg);
  }
}

/* 动态缩放地面微影 */
.allay-shadow {
  width: 44px;
  height: 6px;
  background: radial-gradient(ellipse at center, rgba(14, 165, 233, 0.35) 0%, transparent 70%);
  border-radius: 50%;
  animation: allayShadowScale 2.4s ease-in-out infinite;
  margin-top: 4px;
  z-index: 1;
}

@keyframes allayShadowScale {
  0%, 100% {
    transform: scale(1.15);
    opacity: 0.6;
  }
  30% {
    transform: scale(0.75);
    opacity: 0.25;
  }
  65% {
    transform: scale(0.95);
    opacity: 0.45;
  }
}

/* 漂浮闪光星星 */
.sparkle {
  position: absolute;
  color: #38bdf8;
  font-size: 11px;
  pointer-events: none;
  animation: sparkleTwinkle 2.2s ease-in-out infinite;
}

.sparkle-1 {
  top: 4px;
  right: 6px;
  color: #38bdf8;
  animation-delay: 0s;
}

.sparkle-2 {
  top: 18px;
  left: 4px;
  font-size: 9px;
  color: #a78bfa;
  animation-delay: 0.8s;
}

.sparkle-3 {
  bottom: 14px;
  right: 2px;
  font-size: 8px;
  color: #34d399;
  animation-delay: 1.5s;
}

@keyframes sparkleTwinkle {
  0%, 100% {
    transform: scale(0.4) rotate(0deg);
    opacity: 0.2;
  }
  50% {
    transform: scale(1.25) rotate(45deg);
    opacity: 0.95;
  }
}

/* === 底部微进度条 === */
@keyframes shimmerSlide {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(250%);
  }
}

.loading-shimmer-bar {
  width: 50%;
  height: 100%;
  border-radius: 9999px;
  background: linear-gradient(90deg, transparent, var(--td-brand-color), transparent);
  animation: shimmerSlide 1.6s cubic-bezier(0.4, 0, 0.2, 1) infinite;
}
</style>
