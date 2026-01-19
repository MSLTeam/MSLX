<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import type { FormInstanceFunctions, FormRule } from 'tdesign-vue-next';
import { useUpdateStore, useUserStore } from '@/store';
import NotificationPlugin from 'tdesign-vue-next/es/notification/plugin';
import { request } from '@/utils/request';

const userStore = useUserStore();
const updateStore = useUpdateStore();
const router = useRouter();
const route = useRoute();

// 定义 LocalStorage Key
const REMEMBER_URL = 'remembered_url';
const REMEMBER_USER = 'remembered_username';

// 状态控制
const loading = ref(false);
const showPsw = ref(false);
const isLocalBackend = ref(false); // 是否是同源后端
const isChecking = ref(true); // 是否正在检测连接
const showForgetModal = ref(false); // 控制忘记密码弹窗

// 表单数据
const formData = reactive({
  url: localStorage.getItem(REMEMBER_URL) || '',
  username: localStorage.getItem(REMEMBER_USER) || '',
  password: '',
  checked: !!localStorage.getItem(REMEMBER_USER), // 记住用户名开关
});

const form = ref<FormInstanceFunctions>();

// 动态校验规则
const formRules = computed((): Record<string, FormRule[]> => {
  const rules: Record<string, FormRule[]> = {
    username: [{ required: true, message: '请输入用户名', type: 'error' }],
    password: [{ required: true, message: '请输入密码', type: 'error' }],
  };
  // 只有非同源才校验 URL
  if (!isLocalBackend.value) {
    rules.url = [{ required: true, message: '请输入服务器地址', type: 'error' }];
  }
  return rules;
});

// 初始化检测：判断后端是否在本地
const initCheck = async () => {
  isChecking.value = true;
  // 检测同源
  const isLocal = await userStore.checkConnection('');

  if (isLocal) {
    isLocalBackend.value = true;
    formData.url = '';
  } else {
    isLocalBackend.value = false;
  }
  isChecking.value = false;
};

// 提交登录
const onSubmit = async ({ validateResult }) => {
  if (validateResult === true) {
    loading.value = true;
    try {
      await userStore.login({
        url: isLocalBackend.value ? '' : formData.url, // 同源传空，异地传值
        // url: isLocalBackend.value ? window.location.origin : formData.url, // 同源传空，异地传值
        username: formData.username,
        password: formData.password,
        checked: formData.checked,
      });

      MessagePlugin.success('登录成功');

      // 跳转逻辑
      const redirect = route.query.redirect as string;
      const redirectUrl = redirect ? decodeURIComponent(redirect) : '/dashboard/base';
      router.push(redirectUrl);

      NotificationPlugin.success({
        content: `欢迎回来！${userStore.userInfo.name}`,
        title: 'MSLX 控制台',
      });

      // 检查更新
      updateStore.checkAppUpdate(false);
    } catch (e: any) {
      MessagePlugin.error(e.message || '登录失败，请检查账号密码');
    } finally {
      loading.value = false;
    }
  }
};

// MSL登录
const handleMSLLogin = async () => {
  try {
    loading.value = true;

    // state
    const state = Math.random().toString(36).substring(2, 15) + Math.random().toString(36).substring(2, 15);
    localStorage.setItem('oauth_state', state);

    const callbackUrl = `${window.location.origin}/oauth/callback?mode=login`;

    // 请求后端获取 OAuth 跳转链接
    const res: any = await request.get({
      url: '/api/auth/oauth/url',
      params: {
        state: state,
        callback: callbackUrl,
      },
    });

    if (res && res.url) {
      window.location.href = res.url;
    } else {
      throw new Error(res.message || '获取授权地址失败');
    }
  } catch (e: any) {
    MessagePlugin.error(e.message || '无法连接到认证服务器');
    loading.value = false;
  }
};

onMounted(() => {
  initCheck();
});
</script>

<template>
  <div>
    <t-form ref="form" class="login-form" :data="formData" :rules="formRules" label-width="0" @submit="onSubmit">
      <div v-if="isChecking" class="loading-wrapper">
        <t-loading text="正在连接服务..." size="small" />
      </div>

      <div v-else class="input-group">
        <t-form-item v-if="!isLocalBackend" name="url">
          <t-input v-model="formData.url" size="large" placeholder="服务器地址 (如 localhost:1027)" class="glass-input">
            <template #prefix-icon><t-icon name="server" /></template>
          </t-input>
        </t-form-item>

        <t-form-item name="username">
          <t-input v-model="formData.username" size="large" placeholder="请输入用户名" class="glass-input">
            <template #prefix-icon><t-icon name="user" /></template>
          </t-input>
        </t-form-item>

        <t-form-item name="password">
          <t-input
            v-model="formData.password"
            size="large"
            :type="showPsw ? 'text' : 'password'"
            placeholder="请输入密码"
            class="glass-input"
          >
            <template #prefix-icon><t-icon name="lock-on" /></template>
            <template #suffix-icon>
              <t-icon :name="showPsw ? 'browse' : 'browse-off'" style="cursor: pointer" @click="showPsw = !showPsw" />
            </template>
          </t-input>
        </t-form-item>
      </div>

      <div class="check-container">
        <t-checkbox v-model="formData.checked">记住用户名</t-checkbox>
        <t-link theme="primary" hover="color" @click="showForgetModal = true"> 忘记密码？ </t-link>
      </div>

      <t-form-item class="btn-container">
        <div class="btn-wrapper">
          <t-button block size="large" type="submit" class="login-btn" :loading="loading"> 登 录 </t-button>

          <div class="msl-login-wrapper">
            <a class="msl-link-btn" @click="handleMSLLogin">
              <t-icon name="user-transmit" />
              <span>使用 MSL 账户登录</span>
            </a>
          </div>
        </div>
      </t-form-item>
    </t-form>
    <t-dialog v-model:visible="showForgetModal" header="找回或重置密码" :footer="false" width="480px" attach="body">
      <div class="reset-guide">
        <div class="guide-item">
          <div class="guide-title"><t-icon name="user-talk" /> 方式一：联系管理员</div>
          <p class="guide-desc">如果系统中存在其他管理员账号，请联系对应人员协助您在后台重置密码。</p>
        </div>

        <t-divider dashed style="margin: 16px 0" />

        <div class="guide-item">
          <div class="guide-title"><t-icon name="refresh" /> 方式二：初始化默认账户</div>
          <p class="guide-desc">若无法联系其他管理员，请在服务器端删除以下配置文件：</p>
          <div class="code-block">DaemonData/Configs/UserList.json</div>
          <p>
            <t-alert style="margin-top: 10px"
              >操作提示：删除该文件后，请<strong>重启守护进程</strong>。系统将自动重新创建包含默认账号密码的初始文件。</t-alert
            >
          </p>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style lang="less" scoped>
.login-form {
  .loading-wrapper {
    display: flex;
    justify-content: center;
    padding: 20px 0;
  }

  .input-group {
    display: flex;
    flex-direction: column;
    gap: 16px;
  }

  // 输入框样式
  :deep(.t-input) {
    border-radius: 8px;
    box-shadow: none;
    transition: all 0.3s;
    background-color: rgba(255, 255, 255, 0.6); // 微透明背景

    &:hover,
    &:focus-within {
      background-color: rgba(255, 255, 255, 0.95);
    }
  }

  .check-container {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin: 16px 0 24px;
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

    &:hover {
      background-color: #f2f2f2;
      transform: scale(1.02);
    }
  }

  .btn-container {
    // 让表单项内容区域占满宽度
    :deep(.t-form__content) {
      width: 100%;
    }
  }

  // 新增：包裹容器样式
  .btn-wrapper {
    width: 100%;
    display: flex;
    flex-direction: column; // 强制垂直排列
  }

  // 调整一下间距
  .msl-login-wrapper {
    margin-top: 16px; // 登录按钮和下方文字的间距
    width: 100%;
    display: flex;
    justify-content: center;
  }

  // --- 新增：简约动态文本按钮样式 ---

  .msl-login-wrapper {
    margin-top: 20px; // 与登录按钮拉开一点距离
    width: 100%;
    display: flex;
    justify-content: center;
  }

  .msl-link-btn {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    font-size: 14px;
    color: var(--td-text-color-secondary); // 默认灰色，不抢眼
    cursor: pointer;
    padding: 4px 0;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);

    // 图标样式
    :deep(.t-icon) {
      font-size: 16px;
      margin-right: 6px;
      transition: transform 0.3s;
    }

    // 关键动效：伪元素下划线
    &::after {
      content: '';
      position: absolute;
      bottom: 0;
      left: 50%; // 从中间开始
      width: 0; // 初始宽度为0
      height: 2px;
      background-color: var(--td-brand-color); // 品牌色下划线
      transition: all 0.3s ease-in-out; // 丝滑动画
      transform: translateX(-50%); // 居中修正
      opacity: 0;
    }

    // 悬浮状态
    &:hover {
      color: var(--td-brand-color); // 文字变色

      // 图标轻微上浮
      :deep(.t-icon) {
        transform: translateY(-1px);
      }

      // 下划线展开
      &::after {
        width: 100%;
        opacity: 1;
      }
    }

    // 点击时的按压感
    &:active {
      opacity: 0.8;
      transform: scale(0.98);
    }
  }
}

// 适配亮色主题
:global(.light) .login-btn {
  background-color: var(--td-brand-color);
  color: #fff;
}

// 重置密码弹窗
.reset-guide {
  padding: 8px 4px;

  .guide-item {
    .guide-title {
      font-weight: 600;
      font-size: 15px;
      color: var(--td-text-color-primary);
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;
    }

    .guide-desc {
      font-size: 13px;
      color: var(--td-text-color-secondary);
      line-height: 1.6;
      margin-bottom: 8px;
    }

    .code-block {
      background-color: var(--td-bg-color-secondarycontainer);
      padding: 8px 12px;
      border-radius: 6px;
      font-family: monospace;
      color: var(--td-brand-color);
      font-size: 13px;
      word-break: break-all;
      border: 1px dashed var(--td-component-border);
    }
  }
}
</style>
