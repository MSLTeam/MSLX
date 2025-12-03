<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import type { FormInstanceFunctions, FormRule } from 'tdesign-vue-next';
import { useUserStore } from '@/store';

const userStore = useUserStore();
const REMEMBER_URL_NAME = 'remembered_url';
const REMEMBER_KEY_NAME = 'remembered_key';

const INITIAL_DATA = {
  url: localStorage.getItem(REMEMBER_URL_NAME) || 'localhost:1027',
  key: localStorage.getItem(REMEMBER_KEY_NAME) || '',
  checked: !!localStorage.getItem(REMEMBER_URL_NAME),
};

const FORM_RULES: Record<string, FormRule[]> = {
  url: [{ required: true, message: '连接地址必填', type: 'error' }],
  key: [{ required: true, message: '密钥必填', type: 'error' }],
};

const form = ref<FormInstanceFunctions>();
const formData = ref({ ...INITIAL_DATA });
const showPsw = ref(false);

const router = useRouter();
const route = useRoute();

const handleLoginSuccess = () => {
  MessagePlugin.success('登陆成功');
  const redirect = route.query.redirect as string;
  const redirectUrl = redirect ? decodeURIComponent(redirect) : '/dashboard/base';
  router.push(redirectUrl);

  if (formData.value.checked) {
    localStorage.setItem(REMEMBER_URL_NAME, formData.value.url);
    localStorage.setItem(REMEMBER_KEY_NAME, formData.value.key);
  } else {
    localStorage.removeItem(REMEMBER_URL_NAME);
    localStorage.removeItem(REMEMBER_KEY_NAME);
  }
};

const onSubmit = async ({ validateResult }) => {
  if (validateResult === true) {
    try {
      await userStore.login(formData.value);
      handleLoginSuccess();
    } catch (e: any) {
      console.log(e);
      MessagePlugin.error(e.message || '登录失败');
    }
  }
};

const checkAutoLogin = async () => {
  const authParam = route.query.auth as string;
  if (!authParam) return;
  try {
    const decodedStr = window.atob(authParam);
    const lastIndex = decodedStr.lastIndexOf('|');
    if (lastIndex === -1) {
      MessagePlugin.warning('自动登录链接无效');
      return;
    }
    const url = decodedStr.substring(0, lastIndex);
    const key = decodedStr.substring(lastIndex + 1);
    if (!url || !key) return;
    formData.value.url = url;
    formData.value.key = key;
    const msgInstance = MessagePlugin.loading('检测到登录参数，正在自动登录...');
    await userStore.login(formData.value);
    MessagePlugin.close(msgInstance);
    handleLoginSuccess();
  } catch (e: any) {
    console.error('Auto login failed:', e);
    MessagePlugin.error('自动登录链接无效或已过期');
  }
};

onMounted(() => {
  checkAutoLogin();
});
</script>

<template>
  <t-form
    ref="form"
    class="login-form"
    :data="formData"
    :rules="FORM_RULES"
    label-width="0"
    @submit="onSubmit"
  >
    <div class="input-group">
      <t-form-item name="url">
        <t-input
          v-model="formData.url"
          size="large"
          placeholder="请输入MSLX连接地址"
          class="glass-input"
        >
          <template #prefix-icon>
            <t-icon name="api" />
          </template>
        </t-input>
      </t-form-item>

      <t-form-item name="key">
        <t-input
          v-model="formData.key"
          size="large"
          :type="showPsw ? 'text' : 'password'"
          clearable
          placeholder="请输入密钥"
          class="glass-input"
        >
          <template #prefix-icon>
            <t-icon name="lock-on" />
          </template>
          <template #suffix-icon>
            <t-icon :name="showPsw ? 'browse' : 'browse-off'" @click="showPsw = !showPsw" style="cursor: pointer" />
          </template>
        </t-input>
      </t-form-item>
    </div>

    <div class="check-container">
      <t-checkbox v-model="formData.checked">记住连接信息</t-checkbox>
    </div>

    <t-form-item class="btn-container">
      <t-button block size="large" type="submit" class="login-btn">
        确认登录
      </t-button>
    </t-form-item>
  </t-form>
</template>

<style lang="less" scoped>
.login-form {
  .input-group {
    display: flex;
    flex-direction: column;
    gap: 16px; // 输入框之间的间距
  }

  :deep(.t-input) {
    border-radius: 8px;
    box-shadow: none;
    transition: all 0.3s;
  }

  .check-container {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin: 16px 0 32px;

    :deep(.t-checkbox__label) {
      opacity: 0.9;
    }
  }

  .login-btn {
    border-radius: 24px; // 圆角按钮
    font-weight: bold;
    height: 48px;
    font-size: 16px;
    background-color: #fff; // 默认白色
    color: #333; // 默认深色字
    border: none;
    transition: transform 0.2s;

    &:hover {
      background-color: #f2f2f2;
      transform: scale(1.02);
    }
  }
}


:global(.light) .login-btn {
  background-color: var(--td-brand-color);
  color: #fff;
}
</style>
