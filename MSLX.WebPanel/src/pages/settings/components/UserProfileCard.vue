<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  FileCopyIcon,
  RefreshIcon,
  LockOnIcon,
  CheckCircleIcon,
  TimeIcon,
  UserIcon,
  ImageIcon,
  LogoQqIcon,
  LinkIcon
} from 'tdesign-icons-vue-next';

import { getSelfInfo, updateSelfInfo } from '@/api/user';
import type { UserInfoModel, UpdateUserRequest } from '@/api/model/user';
import { useUserStore } from '@/store';

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
    }
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
      resetApiKey: false
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
        }
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
</script>

<template>
  <t-card :bordered="false" :loading="loading" class="settings-card">
    <div class="profile-header">
      <div class="avatar-col">
        <t-avatar :image="userInfo.avatar" size="80px" shape="circle" class="user-avatar-shadow">
          {{ userInfo.name ? userInfo.name.slice(0, 1).toUpperCase() : 'U' }}
        </t-avatar>
      </div>
      <div class="info-col">
        <div class="main-row">
          <span class="nickname">{{ userInfo.name || '未设置昵称' }}</span>
          <t-tag v-if="userInfo.role === 'admin'" theme="success" variant="light" size="small" shape="round">管理员</t-tag>
          <t-tag v-else theme="primary" variant="light" size="small" shape="round">普通用户</t-tag>
        </div>
        <div class="sub-row">
          <span class="username">@{{ userInfo.username }}</span>
          <span v-if="userInfo.lastLoginTime" class="last-login">
            <time-icon /> {{ new Date(userInfo.lastLoginTime).toLocaleString() }}
          </span>
        </div>
      </div>
    </div>

    <t-divider />

    <t-form ref="userForm" :data="userInfo" :label-width="100" label-align="left" @submit="onUserSubmit">

      <t-form-item label="头像设置">
        <t-space direction="vertical" style="width: 100%">
          <t-radio-group v-model="avatarMode" variant="default-filled" @change="handleModeChange">
            <t-radio-button value="qq"><logo-qq-icon /> QQ头像</t-radio-button>
            <t-radio-button value="custom"><link-icon /> 链接</t-radio-button>
          </t-radio-group>

          <t-input v-if="avatarMode === 'qq'" v-model="qqNumber" placeholder="输入QQ号自动获取" type="number">
            <template #prefix-icon><user-icon /></template>
          </t-input>
          <t-input v-else v-model="userInfo.avatar" placeholder="输入图片 URL 链接">
            <template #prefix-icon><image-icon /></template>
          </t-input>
        </t-space>
      </t-form-item>

      <t-form-item label="用户昵称" name="name">
        <t-input v-model="userInfo.name" placeholder="显示的名称" />
      </t-form-item>

      <t-form-item label="登录账号" name="username">
        <t-input v-model="userInfo.username" placeholder="登录唯一标识" />
      </t-form-item>

      <t-form-item label="API Key" help="用于第三方工具连接的凭证，请妥善保管">
        <t-input
          :value="userInfo.apiKey"
          :type="showApiKey ? 'text' : 'password'"
          readonly
          placeholder="点击重置生成 Key"
        >
          <template #suffix>
            <t-button variant="text" size="small" @click="copyApiKey">
              <template #icon><file-copy-icon /></template>
            </t-button>
            <t-button variant="text" theme="danger" size="small" @click="resetApiKey">
              <template #icon><refresh-icon /></template>
            </t-button>
          </template>
        </t-input>
      </t-form-item>

      <t-divider dashed />

      <t-form-item label="修改密码">
        <t-switch v-model="securityState.changePassword" />
      </t-form-item>

      <template v-if="securityState.changePassword">
        <t-form-item label="新密码" required-mark>
          <t-input
            v-model="securityState.newPassword"
            type="password"
            placeholder="请输入新密码"
          >
            <template #prefix-icon><lock-on-icon /></template>
          </t-input>
        </t-form-item>

        <t-form-item label="确认密码" required-mark>
          <t-input
            v-model="securityState.confirmPassword"
            type="password"
            placeholder="请再次输入新密码"
          >
            <template #prefix-icon><check-circle-icon /></template>
          </t-input>
        </t-form-item>
      </template>

      <t-form-item>
        <t-button theme="primary" type="submit" :loading="submitLoading" block class="action-btn">
          保存个人资料
        </t-button>
      </t-form-item>
    </t-form>
  </t-card>
</template>

<style scoped lang="less">
.settings-card {
  border-radius: 8px;
  overflow: hidden;
  transition: all 0.3s;
  &:hover { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05); }
}

.profile-header {
  display: flex;
  align-items: center;
  padding: 8px 0;
  gap: 24px;

  .user-avatar-shadow {
    box-shadow: 0 4px 10px rgba(0, 0, 0, 0.1);
    border: 2px solid var(--td-bg-color-container);
  }

  .info-col {
    display: flex;
    flex-direction: column;
    gap: 4px;

    .main-row {
      display: flex;
      align-items: center;
      gap: 12px;
      .nickname {
        font-size: 20px;
        font-weight: 700;
        color: var(--td-text-color-primary);
      }
    }

    .sub-row {
      display: flex;
      align-items: center;
      gap: 16px;
      font-size: 13px;
      color: var(--td-text-color-secondary);
      .username {
        font-family: monospace;
        background: var(--td-bg-color-secondarycontainer);
        padding: 2px 6px;
        border-radius: 4px;
      }
      .last-login {
        display: flex;
        align-items: center;
        gap: 4px;
        opacity: 0.8;
      }
    }
  }
}

.action-btn {
  margin-top: 12px;
  font-weight: 600;
  height: 40px;
}
</style>
