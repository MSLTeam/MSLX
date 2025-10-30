<script setup lang="ts">
// 1. 导入 onMounted
import { ref, onMounted } from 'vue';
import { Card as TCard, Icon as TIcon } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import { request } from '@/utils/request'

const userStore = useUserStore();

// 3. 初始化 oneMsg
const oneMsg = ref(''); // 默认显示加载中

// 4. 定义获取一言的函数（从旧组件迁移）
async function getHitokoto() {
  // 发生错误时的备用“一言”
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

// 5. 在组件挂载时调用
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
// -------------------------------------
// 样式迁移 (已适配 TDesign CSS 变量)
// -------------------------------------

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
  // 使用 TDesign 的品牌色变量
  border: 2px solid var(--td-brand-color);
  // 使用 TDesign 的阴影变量
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
  // 使用 TDesign 的一级文本颜色变量
  color: var(--td-text-color-primary);
  font-size: var(--td-font-size-xl); // 适配 TDesign 字号
  font-weight: 600;

  .welcome-icon {
    // 使用 TDesign 的警告色（橙色）变量
    color: var(--td-warning-color);
    font-size: 1.2em; // 图标稍大
  }
}

.hitokoto-text {
  margin: 0;
  // 使用 TDesign 的二级（次要）文本颜色变量
  color: var(--td-text-color-secondary);
  font-size: var(--td-font-size-m);
  font-style: italic; // 保留旧组件的斜体样式
  line-height: 1.6;
}

// -------------------------------------
// 响应式布局 (从旧组件迁移)
// -------------------------------------
@media screen and (max-width: 768px) {
  .welcome-header {
    flex-direction: column;
    text-align: center;
  }

  .welcome-info {
    text-align: center;
  }
}
</style>
