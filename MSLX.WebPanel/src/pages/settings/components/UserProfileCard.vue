<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  RefreshIcon,
  LockOnIcon,
  CheckCircleIcon,
  TimeIcon,
  UserIcon,
  ImageIcon,
  LogoQqIcon,
  LinkIcon,
} from 'tdesign-icons-vue-next';

import { getSelfInfo, updateSelfInfo } from '@/api/user';
import type { UserInfoModel, UpdateUserRequest } from '@/api/model/user';
import { useUserStore } from '@/store';
import { request } from '@/utils/request';
import { isInternalNetwork } from '@/utils/tools';

const userStore = useUserStore();

const loading = ref(false);
const submitLoading = ref(false);
const showApiKey = ref(false);
const originalUsername = ref('');
const qqNumber = ref('');
const avatarMode = ref<'qq' | 'custom'>('qq');

const userInfo = reactive<UserInfoModel>({
  id: '',
  username: '',
  name: '',
  avatar: '',
  role: '',
  apiKey: '',
  lastLoginTime: '',
  openMSLID: '',
});

const securityState = reactive({
  changePassword: false,
  newPassword: '',
  confirmPassword: '',
});

// 初始化数据方法，供父组件调用
const initData = async () => {
  loading.value = true;
  try {
    const userData = await getSelfInfo();
    Object.assign(userInfo, userData);
    originalUsername.value = userData.username;

    // 解析头像模式
    const qqMatch = userData.avatar && userData.avatar.match(/nk=(\d+)/);
    if (qqMatch && qqMatch[1]) {
      avatarMode.value = 'qq';
      qqNumber.value = qqMatch[1];
    } else {
      avatarMode.value = 'custom';
    }
  } catch (e: any) {
    MessagePlugin.error(e.message || '用户加载失败');
  } finally {
    loading.value = false;
  }
};

// 监听 QQ 号变化
watch(qqNumber, (val) => {
  if (avatarMode.value === 'qq' && val) {
    userInfo.avatar = `https://q.qlogo.cn/g?b=qq&nk=${val}&s=640`;
  }
});

const handleModeChange = (val: any) => {
  if (val === 'qq' && qqNumber.value) {
    userInfo.avatar = `https://q.qlogo.cn/g?b=qq&nk=${qqNumber.value}&s=640`;
  }
};

const copyApiKey = () => {
  if (!userInfo.apiKey) return;
  navigator.clipboard.writeText(userInfo.apiKey).then(() => {
    MessagePlugin.success('API Key 已复制');
  });
};

const resetApiKey = () => {
  const confirmDia = DialogPlugin.confirm({
    header: '重置 API 密钥',
    theme: 'warning',
    body: '重置后，所有使用旧 Key 的外部工具将立即失效，确定要继续吗？',
    onConfirm: async () => {
      try {
        confirmDia.hide();
        await updateSelfInfo({ resetApiKey: true });
        MessagePlugin.success('API Key 重置成功');
        const newData = await getSelfInfo();
        userInfo.apiKey = newData.apiKey;
      } catch (e: any) {
        MessagePlugin.error(e.message || '重置失败');
      }
    },
  });
};

const onUserSubmit = async () => {
  if (securityState.changePassword) {
    if (!securityState.newPassword) {
      MessagePlugin.warning('请输入新密码');
      return;
    }
    if (securityState.newPassword !== securityState.confirmPassword) {
      MessagePlugin.error('两次输入的密码不一致');
      return;
    }
  }

  const isUsernameChanged = userInfo.username !== originalUsername.value;
  const isPasswordChanged = securityState.changePassword && !!securityState.newPassword;

  submitLoading.value = true;
  try {
    const updateData: UpdateUserRequest = {
      username: userInfo.username,
      name: userInfo.name,
      avatar: userInfo.avatar,
      password: isPasswordChanged ? securityState.newPassword : undefined,
      resetApiKey: false,
    };

    await updateSelfInfo(updateData);

    securityState.changePassword = false;
    securityState.newPassword = '';
    securityState.confirmPassword = '';
    originalUsername.value = userInfo.username;

    MessagePlugin.success('个人信息保存成功');

    if (isUsernameChanged || isPasswordChanged) {
      DialogPlugin.alert({
        header: '重新登录',
        body: '账号或密码已变更，请重新登录以生效。',
        confirmBtn: '去登录',
        onConfirm: async () => {
          await userStore.logout();
          window.location.reload();
        },
      });
    } else {
      await userStore.getUserInfo();
    }
  } catch (error: any) {
    MessagePlugin.error(error.message);
  } finally {
    submitLoading.value = false;
  }
};

defineExpose({ initData });

// 绑定/解绑MSL账户
const bindLoading = ref(false); // 绑定按钮 loading 状态

const handleBindMSL = async () => {
  bindLoading.value = true;
  try {
    const state = Math.random().toString(36).substring(2, 15) + Math.random().toString(36).substring(2, 15);
    localStorage.setItem('oauth_state', state);
    const callbackUrl = `${window.location.origin}/oauth/callback?mode=bind`;
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
      MessagePlugin.error(res.message || '获取绑定地址失败');
      bindLoading.value = false;
    }
  } catch (e: any) {
    MessagePlugin.error(e.message || '请求失败');
    bindLoading.value = false;
  }
};

const handleUnbindMSL = () => {
  const confirmDia = DialogPlugin.confirm({
    header: '解除绑定',
    theme: 'warning',
    body: '确定要解除与 MSL 账户的绑定吗？解绑后您将无法使用 MSL 账户快捷登录。',
    onConfirm: async () => {
      try {
        confirmDia.hide();
        await request.post({
          url: '/api/auth/oauth/unbind',
        });
        MessagePlugin.success('解绑成功');
        await initData();
      } catch (e: any) {
        MessagePlugin.error(e.message || '解绑失败');
      }
    },
  });
};
</script>

<template>
  <div class="design-card list-item-anim relative flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">

    <t-loading :loading="loading" show-overlay>
      <div class="flex flex-col sm:flex-row items-center sm:items-start gap-6 p-6 sm:p-8 pb-8 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 relative overflow-hidden">

        <div class="absolute -top-10 -right-10 w-40 h-40 bg-[var(--color-primary)]/5 rounded-full blur-3xl pointer-events-none"></div>

        <div class="relative shrink-0">
          <t-avatar :image="userInfo.avatar" size="84px" shape="circle" class="ring-4 ring-white dark:ring-zinc-800 shadow-lg !bg-[var(--color-primary)]/10 !text-[var(--color-primary)] z-10 transition-transform hover:scale-105">
            <span class="font-extrabold text-3xl">{{ userInfo.name ? userInfo.name.slice(0, 1).toUpperCase() : 'U' }}</span>
          </t-avatar>
        </div>

        <div class="flex flex-col items-center sm:items-start gap-2.5 pt-1 z-10 w-full">
          <div class="flex flex-col sm:flex-row items-center gap-3">
            <h1 class="text-2xl font-extrabold tracking-tight text-zinc-900 dark:text-zinc-100 m-0 leading-none">
              {{ userInfo.name || '未设置昵称' }}
            </h1>
            <span v-if="userInfo.role === 'admin'" class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-success)]/10 text-[var(--color-success)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-success)]/20 shadow-sm">
              管理员
            </span>
            <span v-else class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-primary)]/10 text-[var(--color-primary)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-primary)]/20 shadow-sm">
              普通用户
            </span>
          </div>

          <div class="flex flex-col sm:flex-row items-center gap-2 sm:gap-4 text-sm mt-1">
            <div class="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-zinc-100/80 dark:bg-zinc-900/50 border border-zinc-200/50 dark:border-zinc-700/50 text-zinc-600 dark:text-zinc-400 font-mono font-medium shadow-inner">
              <span class="text-zinc-400 dark:text-zinc-500 font-bold">@</span>{{ userInfo.username }}
            </div>

            <div v-if="userInfo.lastLoginTime" class="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400 font-medium">
              <time-icon class="opacity-70 text-[var(--color-primary)]" size="14px" />
              上次登录: <span class="font-mono">{{ new Date(userInfo.lastLoginTime).toLocaleString() }}</span>
            </div>
          </div>
        </div>
      </div>

      <div class="p-5 sm:p-6 sm:px-8 pt-6">
        <t-form ref="userForm" :data="userInfo" :label-width="120" label-align="left" @submit="onUserSubmit">

          <t-form-item label="头像设置">
            <div class="flex flex-col items-start gap-3 w-full">
              <t-radio-group v-model="avatarMode" variant="default-filled" @change="handleModeChange">
                <t-radio-button value="qq"><logo-qq-icon class="opacity-80" /> QQ头像</t-radio-button>
                <t-radio-button value="custom"><link-icon class="opacity-80" /> 链接</t-radio-button>
              </t-radio-group>

              <div class="w-full">
                <t-input v-if="avatarMode === 'qq'" v-model="qqNumber" placeholder="输入 QQ 号自动获取头像" type="number">
                  <template #prefix-icon><user-icon class="opacity-60 text-zinc-400" /></template>
                </t-input>
                <t-input v-else v-model="userInfo.avatar" placeholder="请输入图片 URL 链接">
                  <template #prefix-icon><image-icon class="opacity-60 text-zinc-400" /></template>
                </t-input>
              </div>
            </div>
          </t-form-item>

          <t-form-item label="用户昵称" name="name">
            <t-input v-model="userInfo.name" placeholder="设置前台显示的名称" />
          </t-form-item>

          <t-form-item label="登录账号" name="username">
            <t-input v-model="userInfo.username" placeholder="登录唯一标识" />
          </t-form-item>

          <t-form-item label="API Key">
            <template #help>
              <span class="text-[11px] font-medium text-zinc-400 dark:text-zinc-500 mt-1 inline-block">用于 MSLX 桌面版或第三方工具连接的凭证，请妥善保管。</span>
            </template>
            <t-input
              :value="userInfo.apiKey"
              :type="showApiKey ? 'text' : 'password'"
              readonly
              placeholder="点击重置生成全新 Key"
              class="!font-mono !bg-zinc-50/50 dark:!bg-zinc-900/30"
            >
              <template #suffix>
                <div class="flex items-center gap-1">
                  <t-button variant="text" size="small" class="hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)] !h-auto !w-auto !p-1.5 !rounded-md" title="复制" @click="copyApiKey">
                    <t-icon name="file-copy" />
                  </t-button>
                  <div class="w-[1px] h-3 bg-zinc-200 dark:bg-zinc-700 mx-0.5"></div>
                  <t-button variant="text" theme="danger" size="small" class="hover:!bg-red-500/10 hover:!text-red-500 !h-auto !w-auto !p-1.5 !rounded-md" title="重置 Key" @click="resetApiKey">
                    <refresh-icon />
                  </t-button>
                </div>
              </template>
            </t-input>
          </t-form-item>

          <template v-if="!isInternalNetwork()">
            <div class="h-px bg-dashed border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 my-6"></div>

            <t-form-item label="MSL 账户绑定">
              <template #help>
                <span class="text-[11px] font-medium text-zinc-400 dark:text-zinc-500 mt-1.5 inline-block">绑定后可使用 MSL 账户一键快捷登录本控制台。</span>
              </template>

              <div v-if="userInfo.openMSLID && userInfo.openMSLID !== '0'" class="flex items-center gap-3">
                <span class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[var(--color-success)]/10 text-[var(--color-success)] font-bold text-xs border border-[var(--color-success)]/20 shadow-sm">
                  <check-circle-icon size="15px" />
                  已绑定 <span class="font-mono ml-1 opacity-80">(UID: {{ userInfo.openMSLID }})</span>
                </span>
                <t-button theme="danger" variant="text" size="small" class="hover:!bg-red-500/10" @click="handleUnbindMSL">
                  解除绑定
                </t-button>
              </div>

              <div v-else>
                <t-button theme="primary" variant="outline" :loading="bindLoading" class="!border-[var(--color-primary)]/30 hover:!bg-[var(--color-primary)]/10" @click="handleBindMSL">
                  <template #icon><link-icon /></template>
                  绑定 MSL 账户
                </t-button>
              </div>
            </t-form-item>
          </template>

          <div class="h-px bg-dashed border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 my-6"></div>

          <t-form-item label="修改密码">
            <t-switch v-model="securityState.changePassword" />
          </t-form-item>

          <div v-if="securityState.changePassword" class="bg-zinc-50/50 dark:bg-zinc-800/30 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 mt-4 w-full">
            <t-form-item label="新密码" required-mark label-width="80">
              <t-input v-model="securityState.newPassword" type="password" placeholder="请输入新密码">
                <template #prefix-icon><lock-on-icon class="opacity-60 text-zinc-400" /></template>
              </t-input>
            </t-form-item>

            <t-form-item label="确认密码" required-mark label-width="80" class="!mb-0 mt-4">
              <t-input v-model="securityState.confirmPassword" type="password" placeholder="请再次输入新密码确认">
                <template #prefix-icon><check-circle-icon class="opacity-60 text-zinc-400" /></template>
              </t-input>
            </t-form-item>
          </div>

          <div class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60">
            <t-button theme="primary" type="submit" :loading="submitLoading" class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow">
              保存个人资料
            </t-button>
          </div>

        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 首次渲染阶梯滑入动画 */
.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

</style>
