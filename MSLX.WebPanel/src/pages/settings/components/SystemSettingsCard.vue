<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { ServerIcon, ControlPlatformIcon, BookIcon, LinkIcon } from 'tdesign-icons-vue-next';
import { getSettings, updateSettings } from '@/api/settings';
import type { SettingsModel } from '@/api/model/settings';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';
import { copyText } from '@/utils/clipboard';

const loading = ref(false);
const submitLoading = ref(false);

const sysData = reactive<SettingsModel>({
  fireWallBanLocalAddr: false,
  openWebConsoleOnLaunch: true,
  neoForgeInstallerMirrors: 'MSL Mirrors',
  listenHost: 'localhost',
  listenPort: 1027,
  oAuthMSLClientID: '',
  oAuthMSLClientSecret: '',
});

const mirrorOptions = [
  { label: '官方源 (较慢)', value: 'Official' },
  { label: 'MSL镜像源 (推荐)', value: 'MSL Mirrors' },
  { label: 'MSL镜像源 - 备用', value: 'MSL Mirrors Backup' },
];

const emit = defineEmits(['refresh']);

const initData = async () => {
  loading.value = true;
  try {
    const sysRes = await getSettings();
    Object.assign(sysData, sysRes);
  } catch (e: any) {
    MessagePlugin.error(e.message || '系统设置加载失败');
  } finally {
    loading.value = false;
  }
};

const onSysSubmit = async () => {
  submitLoading.value = true;
  try {
    await updateSettings(sysData);
    MessagePlugin.success('系统设置保存成功');
  } catch (error: any) {
    MessagePlugin.error(error.message);
  } finally {
    submitLoading.value = false;
  }
};

const handleRefresh = () => {
  initData();
  emit('refresh'); // 触发父组件更新其他部分
};

defineExpose({ initData });

const callbackUrl = ref('');

onMounted(() => {
  callbackUrl.value = `${window.location.origin}/oauth/callback`;
});
</script>

<template>
  <t-card :bordered="false" title="系统偏好设置" :loading="loading" class="settings-card">
    <template #actions>
      <t-button theme="primary" variant="text" size="small" @click="handleRefresh">刷新数据</t-button>
    </template>

    <t-form ref="sysForm" :data="sysData" :label-width="120" label-align="left" @submit="onSysSubmit">
      <div class="group-title">守护进程</div>

      <t-form-item label="自动打开控制台" help="MSLX 守护进程启动成功后，是否自动登录网页端控制台。">
        <t-switch v-model="sysData.openWebConsoleOnLaunch" />
      </t-form-item>

      <t-form-item label="安装镜像源" help="选择在自动安装 NeoForge / Forge 时所使用的镜像源。" style="margin-top: 6px">
        <t-select v-model="sysData.neoForgeInstallerMirrors" :options="mirrorOptions" />
      </t-form-item>

      <t-divider dashed />

      <div class="group-title">MSL OAuth 2.0</div>

      <t-form-item label="Client ID" style="margin-top: 6px">
        <t-input v-model="sysData.oAuthMSLClientID" placeholder="请输入 Client ID">
          <template #prefix-icon><server-icon /></template>
        </t-input>
      </t-form-item>

      <t-form-item
        label="Client Secret"
        help="配置MSL OAuth 2.0后即可使用您的MSL账号一键登录您的MSLX控制台。"
        style="margin-top: 16px"
      >
        <t-input v-model="sysData.oAuthMSLClientSecret" type="password" placeholder="请输入 Client Secret">
          <template #prefix-icon><control-platform-icon /></template>
        </t-input>
      </t-form-item>

      <t-form-item
        label="回调地址"
        help="请将此地址复制并填入 MSL用户中心 OAuth应用配置的 [回调地址] 中"
        style="margin-top: 16px"
      >
        <t-input :value="callbackUrl" readonly placeholder="正在获取当前域名...">
          <template #prefix-icon><link-icon /></template>
          <template #suffix>
            <t-button variant="text" shape="square" @click="copyText(callbackUrl,true,'回调地址复制成功')">
              <t-icon name="file-copy" />
            </t-button>
          </template>
        </t-input>
      </t-form-item>

      <t-divider dashed />

      <div class="group-title">网络与安全</div>

      <t-form-item label="禁止本地访问" help="开启后将禁止本地回环地址访问，增强安全性。">
        <t-space align="center">
          <t-switch v-model="sysData.fireWallBanLocalAddr" />
          <span class="status-label">{{ sysData.fireWallBanLocalAddr ? '已开启' : '已关闭' }}</span>
        </t-space>
      </t-form-item>

      <t-form-item
        label="监听地址设置"
        help="设置MSLX守护进程的监听地址。(需要重启守护进程生效,若不明白这是干什么的请一定不要修改！)"
        style="margin-top: 6px"
      >
        <t-row :gutter="16" style="width: 100%">
          <t-col :span="6">
            <t-input v-model="sysData.listenHost" placeholder="localhost">
              <template #prefix-icon><server-icon /></template>
            </t-input>
          </t-col>

          <t-col :span="4" style="display: flex; align-items: center">
            <span style="margin-right: 8px; color: var(--td-text-color-secondary)">:</span>
            <t-input v-model="sysData.listenPort" placeholder="1027" style="width: 120px">
              <template #prefix-icon><control-platform-icon /></template>
            </t-input>
          </t-col>
        </t-row>
      </t-form-item>

      <t-form-item style="margin-top: 6px" label="远程访问">
        <t-space align="center">
          <t-button theme="default" @click="changeUrl(DOC_URLS.remote_access)">
            <template #icon>
              <book-icon />
            </template>
            配置远程访问说明</t-button
          >
        </t-space>
      </t-form-item>

      <t-form-item>
        <t-button theme="primary" type="submit" :loading="submitLoading" block class="action-btn">
          保存系统设置
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
  &:hover {
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
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
</style>
