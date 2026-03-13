<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { ServerIcon, ControlPlatformIcon, BookIcon, LinkIcon, CloudDownloadIcon } from 'tdesign-icons-vue-next';
import { getSettings, updateSettings } from '@/api/settings';
import type { SettingsModel } from '@/api/model/settings';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';
import { copyText } from '@/utils/clipboard';
import { useUpdateStore } from '@/store';
import { isInternalNetwork } from '@/utils/tools';

const updateStore = useUpdateStore();

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
  <div class="design-card relative flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">

    <t-loading :loading="loading" show-overlay>
      <div class="p-5 sm:p-6 sm:px-8">

        <div class="flex items-center justify-between mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex items-center gap-3">
            <div class="w-1.5 h-5 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
            <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">系统偏好设置</h2>
          </div>
          <t-button variant="dashed" size="small" class="!bg-transparent" @click="handleRefresh">
            <template #icon><refresh-icon /></template>
            刷新数据
          </t-button>
        </div>

        <t-form ref="sysForm" :data="sysData" :label-width="140" label-align="left" @submit="onSysSubmit">

          <div class="flex items-center gap-3 mt-2 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">守护进程</span>
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <t-form-item label="软件更新">
            <t-button theme="default" :loading="updateStore.loading" class="!bg-zinc-50 dark:!bg-zinc-800/50 hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !border-zinc-200/80 dark:!border-zinc-700/80 !text-zinc-700 dark:!text-zinc-300" @click="updateStore.checkAppUpdate(true)">
              <template #icon><cloud-download-icon class="opacity-70" /></template>
              检查更新
            </t-button>
          </t-form-item>

          <t-form-item label="自动打开控制台">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">MSLX 守护进程启动成功后，是否自动登录网页端控制台。</span>
            </template>
            <t-switch v-model="sysData.openWebConsoleOnLaunch" />
          </t-form-item>

          <t-form-item label="安装镜像源">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">选择在自动安装 NeoForge / Forge 时所使用的镜像源。</span>
            </template>
            <t-select v-model="sysData.neoForgeInstallerMirrors" :options="mirrorOptions" class="!w-full sm:!w-72" />
          </t-form-item>


          <template v-if="!isInternalNetwork()">
            <div class="flex items-center gap-3 mt-8 mb-6">
              <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">MSL OAuth 2.0</span>
              <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
            </div>

            <t-form-item label="Client ID">
              <t-input v-model="sysData.oAuthMSLClientID" placeholder="请输入 Client ID" class="!w-full sm:!w-96">
                <template #prefix-icon><server-icon class="opacity-60 text-zinc-400" /></template>
              </t-input>
            </t-form-item>

            <t-form-item label="Client Secret">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">配置 MSL OAuth 2.0 后即可使用您的 MSL 账号一键登录控制台。</span>
              </template>
              <t-input v-model="sysData.oAuthMSLClientSecret" type="password" placeholder="请输入 Client Secret" class="!w-full sm:!w-96">
                <template #prefix-icon><control-platform-icon class="opacity-60 text-zinc-400" /></template>
              </t-input>
            </t-form-item>

            <t-form-item label="回调地址">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">请将此地址复制并填入 MSL 用户中心 OAuth 应用配置的 [回调地址] 中。</span>
              </template>
              <t-input :value="callbackUrl" readonly placeholder="正在获取当前域名..." class="!w-full sm:!w-96 !bg-zinc-50/50 dark:!bg-zinc-900/30">
                <template #prefix-icon><link-icon class="opacity-60 text-zinc-400" /></template>
                <template #suffix>
                  <t-button variant="text" shape="square" class="hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)] !h-auto !w-auto !p-1.5 !rounded-md" @click="copyText(callbackUrl, true, '回调地址复制成功')">
                    <t-icon name="file-copy" />
                  </t-button>
                </template>
              </t-input>
            </t-form-item>

            <t-form-item label="配置教程">
              <t-button theme="default"  class="!bg-[var(--color-primary)]/10 !text-[var(--color-primary)] !border-none hover:!bg-[var(--color-primary)]/20" @click="changeUrl(DOC_URLS.msl_oauth)">
                <template #icon><book-icon /></template>
                配置 MSL 账号快捷登录教程
              </t-button>
            </t-form-item>
          </template>


          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">网络与安全</span>
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <t-form-item label="禁止本地访问">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">开启后将禁止本地回环地址访问，增强安全性。</span>
            </template>
            <div class="flex items-center gap-3">
              <t-switch v-model="sysData.fireWallBanLocalAddr" />
              <span class="text-[11px] font-extrabold px-2 py-0.5 rounded-md transition-colors" :class="sysData.fireWallBanLocalAddr ? 'bg-[var(--color-primary)]/10 text-[var(--color-primary)] border border-[var(--color-primary)]/20' : 'bg-zinc-100 dark:bg-zinc-800 text-zinc-500 border border-zinc-200 dark:border-zinc-700'">
                {{ sysData.fireWallBanLocalAddr ? '已开启' : '已关闭' }}
              </span>
            </div>
          </t-form-item>

          <t-form-item label="监听地址设置">
            <template #help>
              <span class="text-[11px] font-medium text-amber-500/80 dark:text-amber-500/70 mt-1 inline-block">设置 MSLX 守护进程的监听地址。(需要重启守护进程生效，若不明白这是干什么的请一定不要修改！)</span>
            </template>
            <div class="flex items-center gap-2 w-full sm:w-96">
              <div class="flex-1">
                <t-input v-model="sysData.listenHost" placeholder="localhost">
                  <template #prefix-icon><server-icon class="opacity-60 text-zinc-400" /></template>
                </t-input>
              </div>
              <div class="text-[var(--td-text-color-secondary)] font-extrabold pb-1">:</div>
              <div class="w-24 shrink-0">
                <t-input v-model="sysData.listenPort" placeholder="1027" align="center">
                  <template #prefix-icon><control-platform-icon class="opacity-60 text-zinc-400 hidden sm:block" /></template>
                </t-input>
              </div>
            </div>
          </t-form-item>

          <t-form-item label="远程访问">
            <t-button theme="default" class="!bg-zinc-50 dark:!bg-zinc-800/50 hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !border-zinc-200/80 dark:!border-zinc-700/80 !text-zinc-700 dark:!text-zinc-300" @click="changeUrl(DOC_URLS.remote_access)">
              <template #icon><book-icon class="opacity-70" /></template>
              配置远程访问说明
            </t-button>
          </t-form-item>

          <div class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60">
            <t-button theme="primary" type="submit" :loading="submitLoading" class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow">
              保存系统设置
            </t-button>
          </div>

        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

</style>
