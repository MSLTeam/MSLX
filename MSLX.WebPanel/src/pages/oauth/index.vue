<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon } from 'tdesign-icons-vue-next';
import LoginHeader from '../login/components/Header.vue';
import TdesignSetting from '@/layouts/setting.vue';
import { useUserStore } from '@/store';
import { request } from '@/utils/request';
import { MessagePlugin } from 'tdesign-vue-next';
import { changeUrl } from '@/router';

// 路由与状态库
const route = useRoute();
const router = useRouter();
const userStore = useUserStore();

// 页面状态
type PageStatus = 'loading' | 'success' | 'error';
const status = ref<PageStatus>('loading');
const loadingTip = ref('正在验证身份...');
const errorMsg = ref('');
const successMsg = ref('');
const countdown = ref(3);

// 处理 OAuth 回调
const handleOAuth = async () => {
  const { code, state, mode } = route.query;

  // 基础参数检查
  if (!code || !state) {
    status.value = 'error';
    errorMsg.value = '无效的回调参数，缺少 Code 或 State。';
    return;
  }

  // State 安全校验
  const storedState = localStorage.getItem('oauth_state');
  localStorage.removeItem('oauth_state');

  if (state !== storedState) {
    status.value = 'error';
    errorMsg.value = '安全校验失败 (State Mismatch)，请求可能被篡改。';
    return;
  }

  try {
    if (mode === 'login') {
      await handleLogin(code as string);
    } else if (mode === 'bind') {
      await handleBind(code as string);
    } else {
      throw new Error('未知的操作模式');
    }
  } catch (e: any) {
    status.value = 'error';
    errorMsg.value = e.message || '处理请求时发生未知错误';
  }
};

// --- 登录逻辑 ---
const handleLogin = async (code: string) => {
  loadingTip.value = '正在登录 MSLX...';
  try {
    const res: any = await request.post({
      url: '/api/auth/oauth/login',
      data: { code },
    });
    await userStore.loginByOAuth(res);
    status.value = 'success';
    successMsg.value = `欢迎回来，${userStore.userInfo.name || res.data.userInfo?.name}`;
    startCountdown('/dashboard/base');
  } catch (e) {
    status.value = 'error';
    errorMsg.value = e.message;
  }
};

// --- 绑定逻辑 ---
const handleBind = async (code: string) => {
  loadingTip.value = '正在绑定 MSL 账号...';
  try {
    await request.post({
      url: '/api/auth/oauth/bind',
      data: { code },
    });
    status.value = 'success';
    successMsg.value = '账号绑定成功！';
    await userStore.getUserInfo();
    startCountdown('/settings');
  } catch (err) {
    MessagePlugin.error(err.message);
  }
};

// 倒计时跳转
const startCountdown = (path: string) => {
  const timer = setInterval(() => {
    countdown.value--;
    if (countdown.value <= 0) {
      clearInterval(timer);
      changeUrl(path);
    }
  }, 1000);
};

const goBack = () => {
  changeUrl('/login');
};

onMounted(() => {
  handleOAuth();
});
</script>

<template>
  <div class="login-wrapper">
    <login-header class="login-header-fixed" />

    <div class="login-panel">
      <div class="login-container">
        <div class="title-container">
          <h1 style="margin-bottom: 10px" class="title">MSL 统一身份认证</h1>
        </div>

        <div class="callback-content">
          <div v-if="status === 'loading'" class="status-box">
            <div class="loading-icon-wrapper">
              <t-loading size="large" />
            </div>
            <p class="tip-text">{{ loadingTip }}</p>
          </div>

          <div v-else-if="status === 'success'" class="status-box success">
            <check-circle-filled-icon class="icon-success" />
            <h2 class="status-title">操作成功</h2>
            <p class="desc">{{ successMsg }}</p>
            <p class="sub-desc">{{ countdown }} 秒后自动跳转...</p>
            <div class="btn-group">
              <t-button block size="large" class="login-btn" @click="router.push('/dashboard/base')">
                立即进入
              </t-button>
            </div>
          </div>

          <div v-else class="status-box error">
            <error-circle-filled-icon class="icon-error" />
            <h2 class="status-title">操作失败</h2>

            <div class="error-msg-box">
              {{ errorMsg }}
            </div>

            <div class="btn-group">
              <t-button block size="large" class="login-btn" @click="goBack"> 返回登录页 </t-button>
            </div>
          </div>
        </div>

        <footer class="copyright">Copyright @ 2021-{{ new Date().getFullYear() }} MSLTeam</footer>
      </div>
    </div>
    <tdesign-setting class="tdesign-setting-outside" />
  </div>
</template>

<style lang="less" scoped>
@import url('../login/index.less');

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

// --- 卡片容器 ---
.login-container {
  width: 420px;
  max-width: 90%;
  padding: 40px;
  border-radius: 16px;
  box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.2);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  display: flex;
  flex-direction: column;
  z-index: 10;
  box-sizing: border-box;
  transition:
    transform 0.3s ease,
    background 0.3s ease;

  .title {
    font-size: 28px;
    font-weight: 600;
    margin-bottom: 8px;
    letter-spacing: 1px;
  }
}

// --- 内容区域 ---
.callback-content {
  min-height: 220px;
  display: flex;
  align-items: center; // 垂直居中
  justify-content: center;
  width: 100%;
}

.status-box {
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  animation: fadeIn 0.5s ease;

  .loading-icon-wrapper {
    margin-bottom: 24px;
    transform: scale(1.2);
  }

  .tip-text {
    font-size: 16px;
    color: var(--td-text-color-secondary);
  }

  .status-title {
    font-size: 20px;
    font-weight: 600;
    margin-top: 16px;
    margin-bottom: 8px;
  }

  .desc {
    font-size: 15px;
    color: var(--td-text-color-secondary);
    margin-bottom: 4px;
  }

  .sub-desc {
    font-size: 13px;
    color: var(--td-text-color-placeholder);
    margin-bottom: 24px;
  }

  .error-msg-box {
    background: rgba(255, 88, 88, 0.1);
    color: var(--td-error-color);
    padding: 12px 16px;
    border-radius: 8px;
    font-size: 14px;
    width: 100%;
    margin: 24px 0 32px 0;
    word-break: break-all;
    line-height: 1.5;
  }

  .btn-group {
    width: 100%;
  }

  .icon-success {
    font-size: 56px;
    color: var(--td-success-color);
  }

  .icon-error {
    font-size: 56px;
    color: var(--td-error-color);
  }
}

.login-btn {
  border-radius: 24px;
  font-weight: bold;
  height: 48px;
  font-size: 16px;
  background-color: #fff;
  color: #333;
  border: none;
  transition: transform 0.2s;
  width: 100%;

  &:hover {
    background-color: #f2f2f2;
    transform: scale(1.02);
  }
}

// --- 亮色/暗色模式适配 ---

.light.login-wrapper {
  background-color: rgba(255, 255, 255, 0.2);

  .login-container {
    background: rgba(255, 255, 255, 0.65);
    border: 1px solid rgba(255, 255, 255, 0.4);
    .title,
    .copyright,
    .status-title {
      color: #333;
    }
  }

  .login-btn {
    background-color: var(--td-brand-color);
    color: #fff;
    &:hover {
      opacity: 0.9;
    }
  }
}

// 暗色模式：背景深透
.dark.login-wrapper {
  background-color: rgba(0, 0, 0, 0.2);
  background-blend-mode: overlay;

  .login-container {
    background: rgba(30, 30, 40, 0.5);
    border: 1px solid rgba(255, 255, 255, 0.15);
    .title,
    .sub-title,
    .copyright,
    .status-title {
      color: #fff;
    }
  }

  .login-btn {
    background-color: rgba(255, 255, 255, 0.9);
    color: #000;
  }
}

// 头部定位
.login-header-fixed {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  z-index: 20;
  background: transparent !important;
  box-shadow: none !important;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

// 移动端适配
@media (max-width: 768px) {
  .login-container {
    width: 100%;
    margin: 20px;
    padding: 30px 20px;
  }
}

.tdesign-setting-outside {
  position: fixed;
  top: 20px;
  right: 20px;
  z-index: 100;

  // 移动端适配
  @media (max-width: 768px) {
    top: 10px;
    right: 10px;
  }
}
</style>
