<script setup lang="ts">
import { ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import type { FormInstanceFunctions, FormRule } from 'tdesign-vue-next';
import { useUserStore } from '@/store';

const userStore = useUserStore();

// 记住键名
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

const onSubmit = async ({ validateResult }) => {
  if (validateResult === true) {
    try {
      await userStore.login(formData.value);

      MessagePlugin.success('登陆成功');
      const redirect = route.query.redirect as string;
      const redirectUrl = redirect ? decodeURIComponent(redirect) : '/dashboard';
      router.push(redirectUrl);
    } catch (e) {
      console.log(e);
      MessagePlugin.error(e.message);
    }
  }
};
</script>

<template>
  <t-form
    ref="form"
    :class="['item-container', `login-password`]"
    :data="formData"
    :rules="FORM_RULES"
    label-width="0"
    @submit="onSubmit"
  >
    <t-form-item name="url">
      <t-input v-model="formData.url" size="large" placeholder="请输入MSLX连接地址">
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
      >
        <template #prefix-icon>
          <t-icon name="lock-on" />
        </template>
        <template #suffix-icon>
          <t-icon :name="showPsw ? 'browse' : 'browse-off'" @click="showPsw = !showPsw" />
        </template>
      </t-input>
    </t-form-item>

    <div class="check-container remember-pwd">
      <t-checkbox v-model="formData.checked">记住连接信息</t-checkbox>
    </div>


    <t-form-item class="btn-container">
      <t-button block size="large" type="submit"> 登录 </t-button>
    </t-form-item>


  </t-form>
</template>



<style lang="less" scoped>
@import url('../index.less');
</style>
