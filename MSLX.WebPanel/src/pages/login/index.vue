<script lang="ts">
export default {
  name: 'LoginIndex',
};
</script>
<script setup lang="ts">
import Login from './components/Login.vue';
import LoginHeader from './components/Header.vue';
import TdesignSetting from '@/layouts/setting.vue';
</script>

<template>
  <div class="login-wrapper">
    <login-header class="login-header-fixed" />

    <div class="login-content">
      <div class="login-container">
        <div class="title-container">
          <h1 class="title">连接到 MSLX</h1>
          <p class="sub-title">网页管理中心</p>
        </div>

        <login />

        <footer class="copyright">Copyright @ 2021-{{ new Date().getFullYear() }} MSLTeam</footer>
      </div>
    </div>

    <tdesign-setting class="tdesign-setting-outside" />
  </div>
</template>

<style lang="less" scoped>
@import url('./index.less');

.login-wrapper {
  width: 100vw;
  min-height: 100vh;
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;
  transition: all 0.3s ease;
  background-image: url('@/assets/bg_light_new.jpg');
}

.dark.login-wrapper {
  background-image: url('@/assets/bg_night_new.jpg');
}

// 通用卡片样式
.login-container {
  width: 420px;
  max-width: 90%; // 移动端保护
  padding: 40px;
  border-radius: 16px;
  box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.2); // 阴影增加层次感
  backdrop-filter: blur(12px); // 背景模糊
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  display: flex;
  flex-direction: column;
  z-index: 10;

  transition: transform 0.3s ease, background 0.3s ease;
}

// 标题样式
.title-container {
  text-align: center;
  margin-bottom: 32px;

  .title {
    font-size: 28px;
    font-weight: 600;
    margin-bottom: 8px;
    letter-spacing: 1px;
  }
  .sub-title {
    font-size: 16px;
    opacity: 0.8;
    margin: 0;
  }
}

.copyright {
  text-align: center;
  font-size: 12px;
  margin-top: 24px;
  opacity: 0.6;
}


.light.login-wrapper {
  background-color: rgba(255, 255, 255, 0.2);

  .login-container {
    background: rgba(255, 255, 255, 0.65); // 白色半透明
    border: 1px solid rgba(255, 255, 255, 0.4);

    .title, .sub-title, .copyright {
      color: #333; // 深色文字
    }
  }
}

.dark.login-wrapper {
  // 深色遮罩
  background-color: rgba(0, 0, 0, 0.2);
  background-blend-mode: overlay;

  .login-container {
    background: rgba(30, 30, 40, 0.5); // 深色半透明
    border: 1px solid rgba(255, 255, 255, 0.15); // 微弱的白边

    .title, .sub-title, .copyright {
      color: #fff; // 白色文字
    }
  }

  // 强制覆盖 TDesign 组件样式以适应 Dark 模式
  :deep(.t-input),
  :deep(.t-input__inner),
  :deep(.t-icon) {
    color: #fff; // 输入框文字白色
  }

  :deep(.t-input) {
    background: rgba(0, 0, 0, 0.2) !important; // 输入框背景更深且透明
    border: 1px solid rgba(255, 255, 255, 0.1);

    &:hover, &:focus-within {
      background: rgba(0, 0, 0, 0.4) !important;
      border-color: rgba(255, 255, 255, 0.3);
    }
  }
}

//设置按钮
.tdesign-setting-outside {
  position: fixed;
  top: 20px;
  right: 20px;
  z-index: 100;
}

// 头部定位
.login-header-fixed {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  z-index: 20;
  background: transparent !important; // 头部透明
  box-shadow: none !important;
}

// --- 移动端适配 ---
@media (max-width: 768px) {
  .login-container {
    width: 100%;
    margin: 20px; // 左右留空
    padding: 30px 20px; // 减小内边距
  }

  .title-container .title {
    font-size: 24px;
  }

  // 移动端把设置按钮往下挪一点或者调整位置
  .tdesign-setting-outside {
    top: 10px;
    right: 10px;
  }
}
</style>
