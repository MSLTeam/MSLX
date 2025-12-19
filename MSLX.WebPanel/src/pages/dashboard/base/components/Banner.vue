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
  <t-card shadow :bordered="false" class="welcome-card">

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
// --- 核心卡片样式 (与监控/公告卡片统一) ---
.welcome-card {
  width: 100%;
  transition: all 0.3s;
  border-radius: 6px;
  background-color: var(--td-bg-color-container);

  // 调整 Card 内边距，使其看起来更紧凑或更舒适
  :deep(.t-card__body) {
    padding: var(--td-comp-paddingTB-l) var(--td-comp-paddingLR-l);
  }
}

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
  transition: transform 0.3s;

  // 头像悬浮微动效果
  &:hover {
    transform: rotate(5deg) scale(1.05);
  }
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
    animation: sun-spin 10s linear infinite; // 增加一个小动画
  }
}

.hitokoto-text {
  margin: 0;
  color: var(--td-text-color-secondary);
  font-size: var(--td-font-size-m);
  font-style: italic;
  line-height: 1.6;
}

// 定义太阳旋转动画
@keyframes sun-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
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
