<script lang="ts">
export default {
  name: 'LoginIndex',
};
</script>
<script setup lang="ts">
import Login from './components/Login.vue';
import LoginHeader from './components/Header.vue';
import TdesignSetting from '@/layouts/setting.vue';
import { onMounted, ref } from 'vue';
import { CheckCircleIcon, LockOnIcon, UserCircleIcon } from 'tdesign-icons-vue-next';

// 简单的初始化弹窗
const showInitDialog = ref(false);

const closeInitDialog = () => {
  showInitDialog.value = false;
  const url = new URL(window.location.href);
  url.searchParams.delete('initialize');
  window.history.replaceState({}, '', url);
};

onMounted(() => {
  const params = new URLSearchParams(window.location.search);
  if (params.get('initialize') === 'true') {
    showInitDialog.value = true;
  }
});
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

    <t-dialog
      v-model:visible="showInitDialog"
      :footer="false"
      :close-btn="true"
      width="480px"
      attach="body"
      class="welcome-dialog"
      :on-close="closeInitDialog"
    >
      <template #header>
        <div class="dialog-header-row">
          <span class="emoji-icon">🎉</span>
          <span class="header-text">欢迎使用 MSLX 开服器</span>
        </div>
      </template>

      <div class="welcome-content">
        <t-alert theme="success" class="security-alert">
          <template #message> 您似乎是第一次使用？请查阅以下信息，然后开始享受您的MC开服之旅吧～ </template>
        </t-alert>

        <div class="account-card">
          <div class="info-row">
            <span class="label"><user-circle-icon /> 默认账户:</span>
            <span class="value highlight">mslx</span>
          </div>
          <div class="info-row">
            <span class="label"><lock-on-icon /> 默认密码:</span>
            <span class="value mono">请在MSLX守护进程端控制台查看</span>
          </div>
        </div>

        <t-alert theme="warning" class="security-alert">
          <template #message> 安全提醒：请登录后<b><u>立即修改默认的账户名和密码</u></b>，保障您的服务安全。 </template>
        </t-alert>

        <t-button block theme="primary" size="large" variant="base" @click="closeInitDialog">
          <template #icon><check-circle-icon /></template>
          我已知晓，立即登录
        </t-button>
      </div>
    </t-dialog>
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

  transition:
    transform 0.3s ease,
    background 0.3s ease;
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

    .title,
    .sub-title,
    .copyright {
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

    .title,
    .sub-title,
    .copyright {
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

    &:hover,
    &:focus-within {
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


// 欢迎弹窗样式
:deep(.welcome-dialog) {
  border-radius: 16px;
  overflow: hidden;

  .t-dialog__header {
    padding-top: 32px;
    padding-bottom: 0;
  }

  .t-dialog__body {
    padding: 24px 32px 32px 32px;
  }
}

.dialog-header-row {
  display: flex;
  align-items: center;
  gap: 12px;

  .emoji-icon {
    font-size: 28px;
  }

  .header-text {
    font-size: 20px;
    font-weight: 700;
    color: var(--td-text-color-primary);
  }
}

.welcome-content {
  .welcome-desc {
    color: var(--td-text-color-secondary);
    font-size: 14px;
    line-height: 1.6;
    margin-bottom: 24px;
  }

  // 账号卡片样式
  .account-card {
    background-color: var(--td-bg-color-secondarycontainer);
    padding: 20px;
    border-radius: 8px;
    border: 1px solid var(--td-component-border);
    margin-bottom: 24px;

    .info-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;

      &:last-child {
        margin-bottom: 0;
      }

      .label {
        display: flex;
        align-items: center;
        gap: 8px;
        color: var(--td-text-color-secondary);
        font-size: 14px;
      }

      .value {
        font-weight: 600;
        color: var(--td-text-color-primary);

        &.highlight {
          color: var(--td-brand-color);
          font-size: 16px;
        }

        &.mono {
          font-family: 'Consolas', 'Monaco', monospace; // 等宽字体
          font-size: 12px;
          opacity: 0.8;
        }
      }
    }
  }

  .security-alert {
    margin-bottom: 24px;
    border-radius: 8px;
  }
}
</style>
