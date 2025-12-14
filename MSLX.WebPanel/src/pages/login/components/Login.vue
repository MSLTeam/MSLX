<script setup lang="ts">
import { ref, reactive, onMounted, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import type { FormInstanceFunctions, FormRule } from 'tdesign-vue-next';
import { useUserStore } from '@/store';

const userStore = useUserStore();
const router = useRouter();
const route = useRoute();

// 定义 LocalStorage Key
const REMEMBER_URL = 'remembered_url';
const REMEMBER_USER = 'remembered_username';

// 状态控制
const loading = ref(false);
const showPsw = ref(false);
const isLocalBackend = ref(false); // 是否是同源后端
const isChecking = ref(true);      // 是否正在检测连接

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
        username: formData.username,
        password: formData.password,
        checked: formData.checked,
      });

      MessagePlugin.success('登录成功');

      // 跳转逻辑
      const redirect = route.query.redirect as string;
      const redirectUrl = redirect ? decodeURIComponent(redirect) : '/dashboard/base';
      router.push(redirectUrl);

    } catch (e: any) {
      MessagePlugin.error(e.message || '登录失败，请检查账号密码');
    } finally {
      loading.value = false;
    }
  }
};

onMounted(() => {
  initCheck();
});
</script>

<template>
  <t-form
    ref="form"
    class="login-form"
    :data="formData"
    :rules="formRules"
    label-width="0"
    @submit="onSubmit"
  >
    <div v-if="isChecking" class="loading-wrapper">
      <t-loading text="正在连接服务..." size="small" />
    </div>

    <div v-else class="input-group">
      <t-form-item v-if="!isLocalBackend" name="url">
        <t-input
          v-model="formData.url"
          size="large"
          placeholder="服务器地址 (如 localhost:1027)"
          class="glass-input"
        >
          <template #prefix-icon><t-icon name="server" /></template>
        </t-input>
      </t-form-item>

      <t-form-item name="username">
        <t-input
          v-model="formData.username"
          size="large"
          placeholder="请输入用户名"
          class="glass-input"
        >
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
            <t-icon
              :name="showPsw ? 'browse' : 'browse-off'"
              style="cursor: pointer"
              @click="showPsw = !showPsw"
            />
          </template>
        </t-input>
      </t-form-item>
    </div>

    <div class="check-container">
      <t-checkbox v-model="formData.checked">记住用户名</t-checkbox>
    </div>

    <t-form-item class="btn-container">
      <t-button block size="large" type="submit" class="login-btn" :loading="loading">
        登 录
      </t-button>
    </t-form-item>
  </t-form>
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

    &:hover, &:focus-within {
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
}

// 适配亮色主题
:global(.light) .login-btn {
  background-color: var(--td-brand-color);
  color: #fff;
}
</style>
