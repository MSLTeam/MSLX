<script setup lang="ts">
import { onMounted, reactive, ref, watch } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  FileCopyIcon,
  RefreshIcon,
  LockOnIcon,
  CheckCircleIcon,
  TimeIcon
} from 'tdesign-icons-vue-next';

import { getSettings, updateSettings } from '@/api/settings';
import { getSelfInfo, updateSelfInfo } from '@/api/user';
import type { SettingsModel } from '@/api/model/settings';
import type { UserInfoModel, UpdateUserRequest } from '@/api/model/user';

import { useUserStore } from '@/store';
import { useWebpanelStore } from '@/store/modules/webpanel';

const webpanelStore = useWebpanelStore();
const userStore = useUserStore();

// 上传背景
const handleFileUpload = async (files: any, targetField: 'webPanelStyleLightBackground' | 'webPanelStyleNightBackground') => {
  const rawFile = files[0]?.raw || files.raw;

  if (!rawFile) return;

  const fileName = await webpanelStore.uploadImage(rawFile);
  if (fileName) {
    webpanelStore.settings[targetField] = fileName;
  }
};


const loading = ref(false);
const userLoading = ref(false);
const userSubmitLoading = ref(false);
const sysSubmitLoading = ref(false);

// 控制 API Key 是否明文显示
const showApiKey = ref(false);

const userInfo = reactive<UserInfoModel>({
  id: '',
  username: '',
  name: '',
  avatar: '',
  role: '',
  apiKey: '',
  lastLoginTime: '',
});

const originalUsername = ref('');

const securityState = reactive({
  changePassword: false,
  newPassword: '',
  confirmPassword: '',
});

const avatarMode = ref<'qq' | 'custom'>('qq');
const qqNumber = ref('');

const sysData = reactive<SettingsModel>({
  fireWallBanLocalAddr: false,
  openWebConsoleOnLaunch: true,
  neoForgeInstallerMirrors: 'MSL Mirrors',
  listenHost: 'localhost',
  listenPort: 1027,
});

const mirrorOptions = [
  { label: '官方源 (较慢)', value: 'Official' },
  { label: 'MSL镜像源 (推荐)', value: 'MSL Mirrors' },
  { label: 'MSL镜像源 - 备用', value: 'MSL Mirrors Backup' },
];

const initData = async () => {
  loading.value = true;
  userLoading.value = true;

  try {
    const [userData, sysRes] = await Promise.all([
      getSelfInfo(),
      getSettings(),
      webpanelStore.fetchSettings()
    ]);

    Object.assign(userInfo, userData);
    originalUsername.value = userData.username;

    const qqMatch = userData.avatar && userData.avatar.match(/nk=(\d+)/);
    if (qqMatch && qqMatch[1]) {
      avatarMode.value = 'qq';
      qqNumber.value = qqMatch[1];
    } else {
      avatarMode.value = 'custom';
    }

    Object.assign(sysData, sysRes);

  } catch (e: any) {
    MessagePlugin.error(e.message || '加载失败');
  } finally {
    loading.value = false;
    userLoading.value = false;
  }
};

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

// 复制 API Key
const copyApiKey = () => {
  if (!userInfo.apiKey) return;
  navigator.clipboard.writeText(userInfo.apiKey).then(() => {
    MessagePlugin.success('API Key 已复制');
  });
};

// 重置 API Key
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

  userSubmitLoading.value = true;
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
    userSubmitLoading.value = false;
  }
};

const onSysSubmit = async () => {
  sysSubmitLoading.value = true;
  try {
    await updateSettings(sysData);
    MessagePlugin.success('系统设置保存成功');
  } catch (error: any) {
    MessagePlugin.error(error.message);
  } finally {
    sysSubmitLoading.value = false;
  }
};

onMounted(() => {
  initData();
});
</script>

<template>
  <div class="settings-page">
    <t-space direction="vertical" size="large" style="width: 100%">

      <t-card :bordered="false" :loading="userLoading" class="settings-card">
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
                <t-radio-button value="qq"><t-icon name="logo-qq" /> QQ头像</t-radio-button>
                <t-radio-button value="custom"><t-icon name="link" /> 链接</t-radio-button>
              </t-radio-group>

              <t-input v-if="avatarMode === 'qq'" v-model="qqNumber" placeholder="输入QQ号自动获取" type="number">
                <template #prefix-icon><t-icon name="user" /></template>
              </t-input>
              <t-input v-else v-model="userInfo.avatar" placeholder="输入图片 URL 链接">
                <template #prefix-icon><t-icon name="image" /></template>
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
            <t-button theme="primary" type="submit" :loading="userSubmitLoading" block class="action-btn">
              保存个人资料
            </t-button>
          </t-form-item>
        </t-form>
      </t-card>

      <t-card :bordered="false" title="系统偏好设置" :loading="loading" class="settings-card">
        <template #actions>
          <t-button theme="primary" variant="text" size="small" @click="initData">刷新数据</t-button>
        </template>

        <t-form ref="sysForm" :data="sysData" :label-width="120" label-align="left" @submit="onSysSubmit">
          <div class="group-title">守护进程</div>

          <t-form-item
            label="自动打开控制台"
            help="MSLX 守护进程启动成功后，是否自动登录网页端控制台。"
          >
            <t-switch v-model="sysData.openWebConsoleOnLaunch" />
          </t-form-item>

          <t-form-item
            label="安装镜像源"
            help="选择在自动安装 NeoForge / Forge 时所使用的镜像源。"
            style="margin-top: 6px;"
          >
            <t-select v-model="sysData.neoForgeInstallerMirrors" :options="mirrorOptions" />
          </t-form-item>

          <t-divider dashed />

          <div class="group-title">网络与安全</div>

          <t-form-item
            label="禁止本地访问"
            help="开启后将禁止本地回环地址访问，增强安全性。"
          >
            <t-space align="center">
              <t-switch v-model="sysData.fireWallBanLocalAddr" />
              <span class="status-label">{{ sysData.fireWallBanLocalAddr ? '已开启' : '已关闭' }}</span>
            </t-space>
          </t-form-item>

          <t-form-item
            label="监听地址设置"
            help="设置MSLX守护进程的监听地址。(需要重启守护进程生效,若不明白这是干什么的请一定不要修改！)"
            style="margin-top: 6px;"
          >
            <t-row :gutter="16" style="width: 100%">
              <t-col :span="6">
                <t-input v-model="sysData.listenHost" placeholder="localhost">
                  <template #prefix-icon><t-icon name="server" /></template>
                </t-input>
              </t-col>

              <t-col :span="4" style="display: flex; align-items: center;">
                <span style="margin-right: 8px; color: var(--td-text-color-secondary)">:</span>
                <t-input v-model="sysData.listenPort" placeholder="1027" style="width: 120px">
                  <template #prefix-icon><t-icon name="control-platform" /></template>
                </t-input>
              </t-col>
            </t-row>
          </t-form-item>

          <t-form-item>
            <t-button theme="primary" type="submit" :loading="sysSubmitLoading" block class="action-btn">
              保存系统设置
            </t-button>
          </t-form-item>
        </t-form>
      </t-card>

      <t-card :bordered="false" title="面板自定义样式" description="这里设置的样式需要在面板左上角的样式面板中启用背景美化才会生效哦！" :loading="webpanelStore.loading" class="settings-card">
        <t-form :data="webpanelStore.settings" :label-width="120" label-align="left" @submit="webpanelStore.saveSettings">

          <div class="group-title">背景图片设置</div>
          <t-form-item label="浅色背景" help="留空则使用默认的背景图哦～">
            <t-input v-model="webpanelStore.settings.webPanelStyleLightBackground" placeholder="输入完整URL地址或者在右边上传图片">
              <template #suffix>
                <t-upload
                  theme="custom"
                  action=""
                  :auto-upload="false"
                  :show-file-list="false"
                  accept="image/png, image/jpeg, image/webp"
                  @change="(val) => handleFileUpload(val, 'webPanelStyleLightBackground')"
                >
                  <t-button variant="text" shape="square">
                    <t-icon name="upload" />
                  </t-button>
                </t-upload>
              </template>
            </t-input>
          </t-form-item>

          <t-form-item label="深色背景">
            <t-input v-model="webpanelStore.settings.webPanelStyleNightBackground" placeholder="输入完整URL地址或者在右边上传图片">
              <template #suffix>
                <t-upload
                  theme="custom"
                  :auto-upload="false"
                  :show-file-list="false"
                  accept="image/png, image/jpeg, image/webp"
                  @change="(_, ctx) => handleFileUpload(ctx, 'webPanelStyleNightBackground')"
                >
                  <t-button variant="text" shape="square"><t-icon name="upload" /></t-button>
                </t-upload>
              </template>
            </t-input>
          </t-form-item>

          <t-divider dashed />
          <div class="group-title">透明度调整 (0.1 - 1.0)</div>

          <t-row :gutter="[32, 16]">
            <t-col :span="6">
              <t-form-item label="浅色背景透明度">
                <t-slider v-model="webpanelStore.settings.webPanelStyleLightBackgroundOpacity" :min="0.1" :max="1" :step="0.01" />
              </t-form-item>
            </t-col>
            <t-col :span="6">
              <t-form-item label="浅色组件透明度">
                <t-slider v-model="webpanelStore.settings.webPanelStyleLightComponentsOpacity" :min="0.1" :max="1" :step="0.01" />
              </t-form-item>
            </t-col>
            <t-col :span="6">
              <t-form-item label="深色背景透明度">
                <t-slider v-model="webpanelStore.settings.webPanelStyleDarkBackgroundOpacity" :min="0.1" :max="1" :step="0.01" />
              </t-form-item>
            </t-col>
            <t-col :span="6">
              <t-form-item label="深色组件透明度">
                <t-slider v-model="webpanelStore.settings.webPanelStyleDarkComponentsOpacity" :min="0.1" :max="1" :step="0.01" />
              </t-form-item>
            </t-col>
          </t-row>

          <t-form-item style="margin-top: 24px">
            <t-button theme="primary" type="submit" :loading="webpanelStore.submitLoading" block class="action-btn">
              应用样式设置
            </t-button>
          </t-form-item>
        </t-form>
      </t-card>

    </t-space>
  </div>
</template>

<style scoped lang="less">
.settings-page {
  width: 100%;
}

.settings-card {
  border-radius: 8px;
  overflow: hidden;
  transition: all 0.3s;

  &:hover {
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
  }
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
      gap: 16px; // 间距拉大点
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

.group-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--td-text-color-placeholder);
  margin: 8px 0 16px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.status-label {
  font-size: 13px;
  color: var(--td-text-color-secondary);
}

.action-btn {
  margin-top: 12px;
  font-weight: 600;
  height: 40px;
}

// 重置上传组件默认样式
:deep(.t-input__suffix) {
  display: flex;
  align-items: center;

  .t-upload {
    width: auto;
    display: inline-flex;
    vertical-align: middle;
  }
  .t-upload__content {
    display: flex;
  }

  .t-upload__tips {
    display: none;
  }
}

:deep(.t-upload__single) {
  display: flex;
  align-items: center;
}
</style>
