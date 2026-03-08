<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { Tag as TTag, Icon as TIcon } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import { request } from '@/utils/request'

const userStore = useUserStore();

const oneMsg = ref('');

async function getHitokoto() {
  const fallbackMsg = 'Every little creature in the world has their own piece of paradise.\n' +
    'Plants take root in the boundless earth, and their leaves grow toward the stars.';

  try {
    // 异步请求
    const res = await request.get({
      url: '/?c=a&c=b&c=c&c=d',
      baseURL: 'https://v1.hitokoto.cn'
    });

    if (res && res.hitokoto) {
      // 格式化并赋值
      oneMsg.value = `${res.hitokoto} —— 《${res.from}》${res.from_who ? ` (${res.from_who})` : ``}`;
    } else {
      // 接口OK但数据异常
      oneMsg.value = fallbackMsg;
    }
  } catch (err) {
    // 捕获请求错误
    oneMsg.value = fallbackMsg;
    console.error("获取一言(Hitokoto)失败:", err);
  }
}

onMounted(() => {
  getHitokoto();
});
</script>

<template>
  <div class="design-card w-full bg-white dark:bg-zinc-800 p-5 sm:p-6 rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">
    <div class="flex flex-col sm:flex-row items-center sm:items-start gap-5">

      <div class="shrink-0 relative">
        <img :src="userStore.userInfo.avatar" alt="头像"
             class="w-20 h-20 rounded-full object-cover ring-2 ring-[var(--color-primary)]/30 shadow-md transition-transform duration-300 hover:rotate-6 hover:scale-105" />
      </div>

      <div class="flex-1 min-w-0 flex flex-col justify-center gap-2.5 text-center sm:text-left mt-1">

        <div class="flex flex-wrap items-center justify-center sm:justify-start gap-3">
          <h3 class="text-l sm:text-xl font-bold text-zinc-800 dark:text-zinc-100 m-0 flex items-center gap-2 tracking-tight">
            <t-icon name="sunny" class="text-amber-500 animate-[spin_10s_linear_infinite]" />
            你好，<span class="text-[var(--color-primary)]">{{ userStore.userInfo.name }}</span>
          </h3>
          <t-tag :theme="userStore.isAdmin ? 'success' : 'primary'" shape="round">
            {{ userStore.isAdmin ? '管理员' : '普通用户' }}
          </t-tag>
        </div>

        <p class="text-sm sm:text-base text-zinc-500 dark:text-zinc-400 italic leading-relaxed m-0">
          {{ oneMsg }}
        </p>

      </div>
    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
