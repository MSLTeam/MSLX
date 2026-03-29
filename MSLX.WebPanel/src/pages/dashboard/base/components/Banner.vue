<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { Tag as TTag, Icon as TIcon } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import { request } from '@/utils/request';

const userStore = useUserStore();

const oneMsg = ref('');

// --- 头像特效状态控制 ---
const isAvatarAnimating = ref(false);
const handleAvatarClick = () => {
  if (isAvatarAnimating.value) return;
  isAvatarAnimating.value = true;
  setTimeout(() => {
    isAvatarAnimating.value = false;
  }, 600);
};

async function getHitokoto() {
  const fallbackMsg =
    'Every little creature in the world has their own piece of paradise.\n' +
    'Plants take root in the boundless earth, and their leaves grow toward the stars.';

  try {
    const res = await request.get({
      url: '/?c=a&c=b&c=c&c=d',
      baseURL: 'https://v1.hitokoto.cn',
    });

    if (res && res.hitokoto) {
      oneMsg.value = `${res.hitokoto} —— 《${res.from}》${res.from_who ? ` (${res.from_who})` : ``}`;
    } else {
      oneMsg.value = fallbackMsg;
    }
  } catch (err) {
    oneMsg.value = fallbackMsg;
    console.error('获取一言(Hitokoto)失败:', err);
  }
}

onMounted(() => {
  getHitokoto();
});
</script>

<template>
  <div
    class="design-card w-full bg-white dark:bg-zinc-800 p-5 sm:p-6 rounded-2xl border border-[var(--td-component-border)] shadow-sm transition-all duration-300"
  >
    <div class="flex flex-col sm:flex-row items-center sm:items-start gap-5">
      <div class="shrink-0 relative group cursor-pointer" @click="handleAvatarClick">
        <div
          class="absolute inset-0 rounded-full z-0 pointer-events-none transition-opacity"
          :class="isAvatarAnimating ? 'animate-magic-burst' : 'opacity-0'"
          style="background: radial-gradient(circle, var(--color-primary-light) 0%, transparent 70%)"
        ></div>

        <img
          :src="userStore.userInfo.avatar"
          alt="头像"
          class="w-20 h-20 rounded-full object-cover ring-2 ring-[var(--color-primary)]/30 shadow-md transition-all duration-300 relative z-10"
          :class="[isAvatarAnimating ? 'animate-jelly-pop' : 'group-hover:rotate-6 group-hover:scale-105']"
        />
      </div>

      <div class="flex-1 min-w-0 flex flex-col justify-center gap-2.5 text-center sm:text-left mt-1">
        <div class="flex flex-wrap items-center justify-center sm:justify-start gap-3">
          <h3
            class="text-l sm:text-xl font-bold text-[var(--td-text-color-primary)] m-0 flex items-center gap-2 tracking-tight"
          >
            <t-icon name="sunny" class="text-amber-500 animate-[spin_10s_linear_infinite]" />
            你好，<span class="text-[var(--color-primary)]">{{ userStore.userInfo.name }}</span>
          </h3>
          <t-tag :theme="userStore.isAdmin ? 'success' : 'primary'" shape="round">
            {{ userStore.isAdmin ? '管理员' : '普通用户' }}
          </t-tag>
        </div>

        <p
          class="text-sm sm:text-base text-[var(--td-text-color-secondary)] italic leading-relaxed m-0 whitespace-pre-line"
        >
          {{ oneMsg }}
        </p>
      </div>
    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

@keyframes jellyPop {
  0% {
    transform: scale(1);
  }
  30% {
    transform: scale(0.85);
  }
  50% {
    transform: scale(1.15);
  }
  65% {
    transform: scale(0.95);
  }
  80% {
    transform: scale(1.05);
  }
  100% {
    transform: scale(1);
  }
}

@keyframes magicBurst {
  0% {
    box-shadow: 0 0 0 0 var(--color-primary);
    opacity: 0.8;
    transform: scale(1);
  }
  100% {
    box-shadow: 0 0 0 35px transparent;
    opacity: 0;
    transform: scale(1.2);
  }
}

.animate-jelly-pop {
  animation: jellyPop 0.6s cubic-bezier(0.25, 1, 0.5, 1) both;
}

.animate-magic-burst {
  animation: magicBurst 0.6s cubic-bezier(0.1, 0.8, 0.3, 1) both;
}
</style>
