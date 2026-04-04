<script setup lang="ts">
import { UserCircleIcon, ServerIcon, CloudIcon, AddIcon, PlayCircleIcon, RefreshIcon } from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { openLoginPopup } from '@/utils/popup';
import { onBeforeUnmount, onMounted, ref, computed } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { convertIniToToml, createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import CreateTunnelDialog from './components/CreateTunnelDialog.vue';
import {
  clearStoredChmlFrpUser,
  createDeviceAuthorization,
  deleteChmlFrpTunnel,
  exchangeDeviceCodeForToken,
  fetchChmlFrpTunnelConfig,
  fetchChmlFrpTunnels,
  fetchChmlFrpUserInfo,
  getStoredChmlFrpUser,
  loginWithAccessToken,
  saveStoredChmlFrpUser,
  type ChmlFrpTunnel,
  type ChmlFrpUserInfo,
  type DeviceAuthorizationResponse,
  type DeviceTokenResponse,
  type StoredChmlFrpUser,
} from './auth';

const showCreateDialog = ref(false);
const chmlUser = ref<StoredChmlFrpUser | null>(null);

const loading = ref(false);
const userInfo = ref<ChmlFrpUserInfo | null>(null);
const tunnels = ref<ChmlFrpTunnel[]>([]);
const selectedTunnelId = ref<number | null>(null);

const authSession = ref<DeviceAuthorizationResponse | null>(null);
const popupWindow = ref<Window | null>(null);
const authMessage = ref('将在新标签页中打开授权页面');
const authError = ref('');
const isAuthorizing = ref(false);
const isPolling = ref(false);
let pollingTimer: number | null = null;

const handleCreateSuccess = () => {
  void initDashboardData();
};

const currentTunnel = computed(() => {
  return tunnels.value.find((t) => t.id === selectedTunnelId.value) || null;
});

const hasChmlAuth = computed(() => {
  return Boolean(chmlUser.value?.accessToken || chmlUser.value?.usertoken);
});

onMounted(() => {
  const storedUser = getStoredChmlFrpUser();
  if (storedUser) {
    chmlUser.value = storedUser;
    void initDashboardData(false);
  }
});

onBeforeUnmount(() => {
  stopPolling();
  if (popupWindow.value && !popupWindow.value.closed) {
    popupWindow.value.close();
  }
});

function stopPolling() {
  if (pollingTimer !== null) {
    window.clearTimeout(pollingTimer);
    pollingTimer = null;
  }
  isPolling.value = false;
}

function resetAuthorizationState() {
  stopPolling();
  authSession.value = null;
  authMessage.value = '将在新标签页中打开授权页面';
  authError.value = '';
  isAuthorizing.value = false;
}

function syncStoredUserInfo(nextUserInfo: ChmlFrpUserInfo) {
  const currentUser = getStoredChmlFrpUser() || chmlUser.value;
  const mergedUser: StoredChmlFrpUser = {
    username: nextUserInfo.username,
    usergroup: nextUserInfo.usergroup,
    userimg: nextUserInfo.userimg,
    usertoken: nextUserInfo.usertoken,
    accessToken: currentUser?.accessToken,
    refreshToken: currentUser?.refreshToken,
    accessTokenExpiresAt: currentUser?.accessTokenExpiresAt,
    tokenType: currentUser?.tokenType,
    tunnelCount: nextUserInfo.tunnelCount,
    tunnel: nextUserInfo.tunnel,
  };

  chmlUser.value = mergedUser;
  saveStoredChmlFrpUser(mergedUser);
}

async function finishLogin(
  accessToken: string,
  tokenResponse?: Pick<DeviceTokenResponse, 'refresh_token' | 'expires_in' | 'token_type'>,
) {
  const authedUser = await loginWithAccessToken(accessToken, tokenResponse);
  saveStoredChmlFrpUser(authedUser);
  chmlUser.value = authedUser;
  resetAuthorizationState();
  const loaded = await initDashboardData();
  if (loaded) {
    MessagePlugin.success('ChmlFrp 授权成功');
  }
}

function scheduleTokenPolling(deviceCode: string, intervalSeconds: number) {
  stopPolling();
  pollingTimer = window.setTimeout(() => {
    void pollToken(deviceCode, intervalSeconds);
  }, intervalSeconds * 1000);
}

async function pollToken(deviceCode: string, intervalSeconds: number) {
  isPolling.value = true;
  try {
    const tokenResponse = await exchangeDeviceCodeForToken(deviceCode);

    if (tokenResponse.access_token) {
      if (popupWindow.value) popupWindow.value.close();
      await finishLogin(tokenResponse.access_token, {
        refresh_token: tokenResponse.refresh_token,
        expires_in: tokenResponse.expires_in,
        token_type: tokenResponse.token_type,
      });
      return;
    }

    if (tokenResponse.error === 'authorization_pending') {
      authMessage.value = '请在浏览器中确认授权';
      scheduleTokenPolling(deviceCode, intervalSeconds);
      return;
    }

    if (tokenResponse.error === 'slow_down') {
      authMessage.value = '请求过于频繁，正在自动重试...';
      scheduleTokenPolling(deviceCode, intervalSeconds + 5);
      return;
    }

    if (tokenResponse.error === 'expired_token') {
      stopPolling();
      authError.value = '这次设备授权已过期，请重新开始授权。';
      return;
    }

    if (tokenResponse.error === 'access_denied') {
      stopPolling();
      authError.value = '你已取消本次授权，请重新开始。';
      return;
    }

    throw new Error(tokenResponse.error_description || tokenResponse.error || '获取访问令牌失败');
  } catch (e: any) {
    stopPolling();
    authError.value = e?.message || '授权失败，请稍后重试';
  }
}

async function openAuthorizationPage(session = authSession.value) {
  if (!session) {
    authError.value = '请先开始授权流程';
    return;
  }

  const target = session.verification_uri_complete || session.verification_uri;

  if (!target) {
    authError.value = '账户中心未返回可用的授权地址';
    return;
  }

  popupWindow.value = openLoginPopup(target, 'ChmlFrp 授权登录', 600, 600);
  authMessage.value = '授权弹窗已打开，完成授权后此页面会自动继续';
}

async function startDeviceAuthorization() {
  resetAuthorizationState();
  isAuthorizing.value = true;
  authMessage.value = '正在获取授权信息...';

  try {
    const session = await createDeviceAuthorization();
    authSession.value = session;
    await openAuthorizationPage(session);
    const intervalSeconds = Math.max(Number(session.interval || 5), 1);
    void pollToken(session.device_code, intervalSeconds);
  } catch (e: any) {
    stopPolling();
    authSession.value = null;
    authError.value = e?.message || '启动授权失败';
  } finally {
    isAuthorizing.value = false;
  }
}

async function initDashboardData(showFailureMessage = true) {
  loading.value = true;
  try {
    const [nextUserInfo, nextTunnels] = await Promise.all([fetchChmlFrpUserInfo(), fetchChmlFrpTunnels()]);

    userInfo.value = nextUserInfo;
    tunnels.value = nextTunnels || [];
    syncStoredUserInfo(nextUserInfo);

    if (tunnels.value.length === 0) {
      selectedTunnelId.value = null;
    } else if (!tunnels.value.some((item) => item.id === selectedTunnelId.value)) {
      selectedTunnelId.value = tunnels.value[0].id;
    }

    return true;
  } catch (e: any) {
    const errorMsg = e?.response?.data?.msg || e?.msg || e?.message || '授权已失效或网络异常';
    if (showFailureMessage) {
      MessagePlugin.error(`ChmlFrp 数据加载失败：${errorMsg}`);
    }
    handleLogout(false);
    return false;
  } finally {
    loading.value = false;
  }
}

const isAddingTunnel = ref(false);

// 使用隧道获取配置文件
async function handleUseTunnel() {
  if (!currentTunnel.value) return;
  isAddingTunnel.value = true;

  try {
    const configText = await fetchChmlFrpTunnelConfig(currentTunnel.value.node, currentTunnel.value.name);
    await createFrpTunnel(
      `${currentTunnel.value.name} | ${currentTunnel.value.node}`,
      convertIniToToml(configText),
      'ChmlFrp',
      'toml',
    );
    MessagePlugin.success('配置文件已成功加载');
  } catch (e: any) {
    const errorMsg = e?.response?.data?.msg || e?.msg || e?.message || '未知错误';
    MessagePlugin.error(`获取配置异常: ${errorMsg}`);
  } finally {
    isAddingTunnel.value = false;
  }
}

const handleAddTunnel = () => {
  showCreateDialog.value = true;
};

function handleLogout(showMessage = true) {
  resetAuthorizationState();
  chmlUser.value = null;
  userInfo.value = null;
  tunnels.value = [];
  selectedTunnelId.value = null;
  clearStoredChmlFrpUser();
  if (showMessage) {
    MessagePlugin.success('已断开 ChmlFrp 授权');
  }
}

function handleLogoutConfirm() {
  handleLogout();
}

async function handleRefresh() {
  const loaded = await initDashboardData();
  if (loaded) {
    MessagePlugin.success('数据已更新');
  }
}

const isDeleting = ref(false);
async function handleDeleteTunnel() {
  if (!currentTunnel.value) return;
  isDeleting.value = true;

  try {
    const res: any = await deleteChmlFrpTunnel(currentTunnel.value.id);

    if (res && res.code && res.code !== 200) {
      throw new Error(res.msg || '删除失败');
    }

    MessagePlugin.success('隧道删除成功');
    selectedTunnelId.value = null;
    await initDashboardData();
  } catch (e: any) {
    const errorMsg = e.message || e.response?.data?.msg || e.msg || '未知错误';
    MessagePlugin.error(`删除失败: ${errorMsg}`);
  } finally {
    isDeleting.value = false;
  }
}
</script>

<template>
  <div class="mx-auto pb-6 text-[var(--td-text-color-primary)]">
    <div v-if="!hasChmlAuth" class="flex items-center justify-center min-h-[70vh] list-item-anim">
      <div
        class="design-card relative w-full max-w-md bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-xl p-10 text-center overflow-hidden"
      >
        <div
          class="absolute -top-20 -right-20 w-60 h-60 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"
        ></div>
        <div
          class="absolute -bottom-10 -left-10 w-40 h-40 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"
        ></div>

        <div class="relative z-10 flex flex-col items-center">
          <div
            class="w-20 h-20 bg-[var(--color-primary)]/10 rounded-2xl flex items-center justify-center mb-6 shadow-sm border border-[var(--color-primary)]/20 overflow-hidden p-2"
          >
            <img src="https://panel.chmlfrp.net/favicon.ico" alt="logo" class="w-full h-full object-contain" />
          </div>
          <h2 class="text-2xl font-extrabold text-[var(--td-text-color-primary)] !mb-2 tracking-tight">登录 ChmlFrp</h2>
          <p class="text-sm text-[var(--td-text-color-secondary)] !mb-6 font-medium">
            使用浏览器完成官方授权，MSLX 会自动同步您的 ChmlFrp 账户
          </p>

          <div v-if="!authSession" class="w-full">
            <t-button
              block
              theme="primary"
              size="large"
              :loading="isAuthorizing"
              class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50"
              @click="startDeviceAuthorization"
            >
              <template #icon><user-circle-icon /></template>
              浏览器授权登录
            </t-button>
          </div>

          <div v-else class="w-full">
            <div
              class="rounded-2xl border border-[var(--td-component-border)] bg-[var(--td-bg-color-secondarycontainer)]/70 p-6"
            >
              <div class="text-xs font-bold uppercase tracking-widest text-[var(--td-text-color-secondary)]">
                设备码
              </div>
              <div class="mt-3 text-3xl font-black tracking-[0.3em] text-[var(--td-text-color-primary)]">
                {{ authSession.user_code || '-' }}
              </div>
            </div>

            <div class="mt-4 flex flex-col gap-3">
              <t-button
                block
                theme="primary"
                size="large"
                class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50"
                @click="openAuthorizationPage()"
              >
                重新打开授权页
              </t-button>
            </div>

            <div class="mt-4 flex min-h-[22px] items-center justify-center">
              <t-button
                variant="text"
                size="small"
                :loading="isAuthorizing"
                class="!h-auto !px-0 text-[var(--color-primary)]"
                @click="startDeviceAuthorization"
              >
                重新开始授权
              </t-button>
            </div>
          </div>

          <div
            v-if="authError"
            class="mt-4 w-full rounded-2xl border border-red-200/80 bg-red-50/80 px-4 py-3 text-sm text-red-500 dark:border-red-900/60 dark:bg-red-950/20"
          >
            {{ authError }}
          </div>

          <div class="mt-6 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700 w-full">
            <t-button
              variant="text"
              size="small"
              class="text-zinc-500 hover:text-[var(--color-primary)]"
              @click="changeUrl('https://panel.chmlfrp.net')"
              >ChmlFrp 控制台</t-button
            >
          </div>
        </div>
      </div>
    </div>

    <div v-else id="app-space" class="relative flex flex-col gap-6">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <div
        v-if="userInfo"
        class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5 sm:p-6"
        style="animation-delay: 0s"
      >
        <div
          class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
        >
          <div class="flex items-center gap-3">
            <t-avatar :image="userInfo.userimg" size="medium" shape="round" />
            <div class="flex flex-col">
              <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">ChmlFrp 账户</h3>
              <span class="text-xs text-zinc-500 mt-1">{{ userInfo.email }}</span>
            </div>
          </div>
          <div class="flex items-center gap-2">
            <t-tag
              v-if="userInfo.realname === '已实名'"
              theme="success"
              variant="light-outline"
              class="!rounded-md !font-bold"
              >已实名</t-tag
            >
            <t-tag theme="primary" variant="light-outline" class="!rounded-md !font-bold">{{
              userInfo.usergroup
            }}</t-tag>
            <div class="w-px h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
            <t-popconfirm content="确认断开 ChmlFrp 的连接吗？" @confirm="handleLogoutConfirm">
              <t-button variant="text" theme="danger" size="small" class="!rounded-lg hover:!bg-red-500/10"
                >退出登录</t-button
              >
            </t-popconfirm>
          </div>
        </div>

        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div
            class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800"
          >
            <div
              class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1"
            >
              用户名称
            </div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] truncate">{{ userInfo.username }}</div>
          </div>

          <div
            class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800"
          >
            <div
              class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1"
            >
              隧道配额
            </div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">
              <span class="text-[var(--color-primary)]">{{ userInfo.tunnelCount }}</span> / {{ userInfo.tunnel }}
              <span class="text-sm font-medium text-zinc-500">条</span>
            </div>
          </div>

          <div
            class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800"
          >
            <div
              class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1"
            >
              带宽限制
            </div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">
              {{ userInfo.bandwidth }} <span class="text-sm font-medium text-zinc-500">Mbps</span>
            </div>
          </div>

          <div
            class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800"
          >
            <div
              class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1"
            >
              账户积分
            </div>
            <div class="text-[15px] font-bold text-[var(--color-warning)] font-mono mt-0.5">
              {{ userInfo.integral }}
            </div>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div
          class="lg:col-span-5 xl:col-span-4 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]"
          style="animation-delay: 0.1s"
        >
          <div
            class="flex items-center justify-between p-4 sm:p-5 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0"
          >
            <h3 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">我的隧道</h3>
            <div class="flex items-center gap-1">
              <t-button
                size="small"
                variant="text"
                class="!px-2 hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50"
                :loading="loading"
                @click="handleRefresh"
              >
                <template #icon><refresh-icon /></template>刷新
              </t-button>
              <t-button size="small" theme="primary" class="!px-3 !ml-1 !rounded-lg" @click="handleAddTunnel">
                <template #icon><add-icon /></template>新建
              </t-button>
            </div>
          </div>

          <div class="flex-1 overflow-y-auto custom-scrollbar p-3">
            <div v-if="tunnels.length > 0" class="flex flex-col gap-2">
              <div
                v-for="tunnel in tunnels"
                :key="tunnel.id"
                class="group flex items-center p-3 rounded-xl cursor-pointer transition-all duration-300 border"
                :class="
                  selectedTunnelId === tunnel.id
                    ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/30 shadow-sm'
                    : 'bg-transparent border-transparent hover:bg-zinc-50 dark:hover:bg-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600'
                "
                @click="selectedTunnelId = tunnel.id"
              >
                <div
                  class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0 mr-3 transition-colors"
                  :class="
                    selectedTunnelId === tunnel.id
                      ? 'bg-[var(--color-primary)] text-white shadow-md shadow-[var(--color-primary)]/30'
                      : 'bg-zinc-100 dark:bg-zinc-900 text-[var(--td-text-color-secondary)] group-hover:text-zinc-800 dark:group-hover:text-zinc-200'
                  "
                >
                  <server-icon size="20px" />
                </div>
                <div class="flex-1 min-w-0 mr-3">
                  <div
                    class="font-bold text-sm truncate transition-colors"
                    :class="
                      selectedTunnelId === tunnel.id
                        ? 'text-[var(--color-primary)]'
                        : 'text-[var(--td-text-color-primary)]'
                    "
                  >
                    {{ tunnel.name }}
                  </div>
                  <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">{{ tunnel.node }}</div>
                </div>
                <div class="shrink-0">
                  <t-tag
                    v-if="tunnel.state === 'true' || tunnel.state === true"
                    theme="success"
                    variant="light"
                    size="small"
                    class="!rounded !font-bold !px-1.5"
                    >在线</t-tag
                  >
                  <t-tag
                    v-else
                    theme="default"
                    variant="light"
                    size="small"
                    class="!rounded !font-bold !px-1.5 !text-zinc-500"
                    >离线</t-tag
                  >
                </div>
              </div>
            </div>

            <div v-else class="h-full flex flex-col items-center justify-center opacity-60">
              <server-icon size="32px" class="text-zinc-400 mb-2" />
              <span class="text-sm text-zinc-500 font-medium">暂无隧道，请先新建</span>
            </div>
          </div>
        </div>

        <div
          class="lg:col-span-7 xl:col-span-8 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]"
          style="animation-delay: 0.2s"
        >
          <template v-if="currentTunnel">
            <div
              class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0"
            >
              <div class="flex flex-col min-w-0">
                <h3 class="text-xl font-extrabold text-[var(--td-text-color-primary)] m-0 truncate">
                  {{ currentTunnel.name }}
                </h3>
                <p
                  class="text-xs text-[var(--td-text-color-secondary)] mt-1 truncate font-mono bg-zinc-100 dark:bg-zinc-800/50 w-max px-2 py-0.5 rounded"
                >
                  ID: {{ currentTunnel.id }}
                </p>
              </div>
              <div class="shrink-0">
                <t-popconfirm
                  content="确认删除此隧道吗？将无法恢复！"
                  theme="danger"
                  placement="bottom-right"
                  @confirm="handleDeleteTunnel"
                >
                  <t-button
                    theme="danger"
                    class="!rounded-lg hover:!bg-red-500 hover:!text-white transition-colors"
                    :loading="isDeleting"
                  >
                    <template #icon><t-icon name="delete" /></template>
                    删除隧道
                  </t-button>
                </t-popconfirm>
              </div>
            </div>

            <div class="flex-1 overflow-y-auto custom-scrollbar p-5 sm:p-6">
              <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-2 gap-4">
                <div
                  class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center"
                >
                  <span
                    class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5"
                    >所在节点</span
                  >
                  <span
                    class="text-sm font-bold text-[var(--td-text-color-primary)] truncate"
                    :title="currentTunnel.node"
                    >{{ currentTunnel.node }}</span
                  >
                </div>

                <div
                  class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center"
                >
                  <span
                    class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5"
                    >本地地址</span
                  >
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]"
                    >{{ currentTunnel.localip }}:{{ currentTunnel.nport }}</span
                  >
                </div>

                <div
                  class="p-4 bg-emerald-50/50 dark:bg-emerald-900/20 rounded-xl border border-emerald-200/50 dark:border-emerald-800/30 flex flex-col justify-center"
                >
                  <span
                    class="text-[11px] font-extrabold text-emerald-600/80 dark:text-emerald-500/80 uppercase tracking-widest mb-1.5"
                    >远程信息 (端口/域名)</span
                  >
                  <span class="text-lg font-mono font-extrabold text-emerald-600 dark:text-emerald-400">{{
                    currentTunnel.dorp
                  }}</span>
                </div>

                <div
                  class="p-4 rounded-xl flex flex-col justify-center border transition-colors"
                  :class="
                    currentTunnel.state === 'true' || currentTunnel.state === true
                      ? 'bg-emerald-50/50 dark:bg-emerald-900/10 border-emerald-200/50 dark:border-emerald-800/30'
                      : 'bg-zinc-50/80 dark:bg-zinc-900/50 border-[var(--td-component-border)]'
                  "
                >
                  <span
                    class="text-[11px] font-extrabold uppercase tracking-widest mb-1.5"
                    :class="
                      currentTunnel.state === 'true' || currentTunnel.state === true
                        ? 'text-emerald-600/80 dark:text-emerald-500/80'
                        : 'text-[var(--td-text-color-secondary)]'
                    "
                    >当前状态</span
                  >
                  <div class="flex items-center gap-2">
                    <span
                      v-if="currentTunnel.state === 'true' || currentTunnel.state === true"
                      class="w-2 h-2 rounded-full bg-[var(--color-success)] animate-pulse"
                    ></span>
                    <span
                      class="text-sm font-bold"
                      :class="
                        currentTunnel.state === 'true' || currentTunnel.state === true
                          ? 'text-[var(--color-success)]'
                          : 'text-zinc-500'
                      "
                    >
                      {{ currentTunnel.state === 'true' || currentTunnel.state === true ? '节点在线' : '离线' }}
                    </span>
                  </div>
                </div>
              </div>

              <div class="mt-8">
                <t-button
                  theme="primary"
                  size="large"
                  :loading="isAddingTunnel"
                  block
                  class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow text-base"
                  @click="handleUseTunnel"
                >
                  <template #icon><play-circle-icon /></template>
                  使用此隧道
                </t-button>
              </div>
            </div>
          </template>

          <template v-else>
            <div class="flex-1 flex flex-col items-center justify-center opacity-50 p-6 text-center">
              <div class="w-24 h-24 bg-zinc-100 dark:bg-zinc-800 rounded-full flex items-center justify-center mb-4">
                <cloud-icon size="40px" class="text-zinc-400" />
              </div>
              <h3 class="text-base font-bold text-zinc-700 dark:text-zinc-300 mb-1">未选择隧道</h3>
              <p class="text-sm text-zinc-500">请在左侧列表中选择一个隧道以查看详细信息</p>
            </div>
          </template>
        </div>
      </div>
    </div>

    <create-tunnel-dialog v-if="showCreateDialog" v-model:visible="showCreateDialog" @success="handleCreateSuccess" />
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';

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

@keyframes smoothLoadingGlass {
  from {
    backdrop-filter: blur(0.01px) !important;
    -webkit-backdrop-filter: blur(0.01px) !important;
  }
  to {
    backdrop-filter: blur(4px) !important;
    -webkit-backdrop-filter: blur(4px) !important;
  }
}

/* 滚动条混入 */
.custom-scrollbar {
  .scrollbar-mixin();
}

:deep(.t-loading__overlay) {
  border-radius: 1rem !important;
  background: rgb(255 255 255 / 50%) !important;
  animation: smoothLoadingGlass 0.3s cubic-bezier(0.2, 0.8, 0.2, 1) forwards !important;
}

:global(.dark) :deep(.t-loading__overlay) {
  background: rgb(24 24 27 / 50%) !important;
}
</style>
