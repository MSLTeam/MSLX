<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { Card as TCard, Icon as TIcon } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import { request } from '@/utils/request'

const userStore = useUserStore();

const oneMsg = ref('');

async function getHitokoto() {
  const fallbackMsg = 'Every little creature in the world has their own piece of paradise.\n' +
    'Plants take root in the boundless earth, and their leaves grow toward the stars.';

  try {
    // 异步请求
    // (注意：如果你还没改拦截器，请使用我之前推荐的“智能识别”方案，否则这里会报错)
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
  <t-card :bordered="false" shadow style="width: 100%">

    <div class="welcome-header">

      <div class="avatar-section">
        <img :src="userStore.userInfo.avatar" alt="头像" class="user-avatar" />
      </div>

      <div class="welcome-info">
        <h3 class="welcome-text">
          <t-icon name="sunny" class="welcome-icon" />
          你好, {{ userStore.userInfo.name }}
        </h3>
        <p class="hitokoto-text">{{ oneMsg }}</p>
      </div>

    </div>

  </t-card>
</template>

<style scoped lang="less">

.welcome-header {
  display: flex;
  align-items: center;
  gap: 20px; // Flex子元素间距
  flex-wrap: wrap; // 允许响应式换行
}

.avatar-section {
  flex-shrink: 0; // 防止头像被压缩
}

.user-avatar {
  width: 80px;
  height: 80px;
  border-radius: 50%;
  border: 2px solid var(--td-brand-color);
  box-shadow: var(--td-shadow-1);
}

.welcome-info {
  flex: 1; // 占据剩余空间
  min-width: 200px; // 换行阈值
}

.welcome-text {
  margin: 0 0 8px 0;
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--td-text-color-primary);
  font-size: var(--td-font-size-xl);
  font-weight: 600;

  .welcome-icon {
    color: var(--td-warning-color);
    font-size: 1.2em;
  }
}

.hitokoto-text {
  margin: 0;
  color: var(--td-text-color-secondary);
  font-size: var(--td-font-size-m);
  font-style: italic;
  line-height: 1.6;
}

// -------------------------------------
// 响应式布局
// -------------------------------------
@media screen and (max-width: 768px) {
  .welcome-header {
    flex-direction: column;
    text-align: center;
  }

  .welcome-info {
    text-align: center;
  }

  .welcome-text {
    justify-content: center;
  }
}
</style>
