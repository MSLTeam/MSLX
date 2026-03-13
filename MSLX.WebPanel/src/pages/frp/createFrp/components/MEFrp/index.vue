<script setup lang="ts">
import {
  UserCircleIcon,
  ServerIcon,
  CloudIcon,
  AddIcon,
  PlayCircleIcon,
  RefreshIcon,
  KeyIcon,
  LockOnIcon,
  SecuredIcon,
} from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { onMounted, ref, computed } from 'vue';
import { request } from '@/utils/request';
import { MessagePlugin } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import CreateTunnelDialog from './components/CreateTunnelDialog.vue';

const showCreateDialog = ref(false);
const meUserToken = ref('');

// 数据状态
const loading = ref(false);
const userInfo = ref<any>(null);
const tunnels = ref<any[]>([]);
const nodesMap = ref<Record<number, string>>({}); // 缓存节点ID -> 节点名称
const selectedTunnelId = ref<number | null>(null);

// 登录相关状态
const loginType = ref('password');
const loginForm = ref({
  username: '',
  password: '',
  captchaCallback: '',
  token: '',
});
const isLoggingIn = ref(false);

const handleCreateSuccess = () => {
  initDashboardData();
};

const currentTunnel = computed(() => {
  return tunnels.value.find((t) => t.proxyId === selectedTunnelId.value) || null;
});

const currentNodeName = computed(() => {
  if (!currentTunnel.value) return '';
  return nodesMap.value[currentTunnel.value.nodeId] || `节点 (${currentTunnel.value.nodeId})`;
});

onMounted(() => {
  const token = localStorage.getItem('mefrp-user-token');
  if (token) {
    meUserToken.value = token;
    initDashboardData();
  }
});

// 弹出验证码小窗口
const openCaptchaPage = () => {
  const width = 500;
  const height = 600;
  const left = (window.screen.width - width) / 2;
  const top = (window.screen.height - height) / 2;

  window.open(
    'https://www.mefrp.com/3rdparty/captcha?client=MSLX',
    'MEFrpCaptcha',
    `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes,status=no,toolbar=no,menubar=no,location=no`,
  );
  MessagePlugin.info('请在弹出的独立窗口中完成验证，并将获取到的验证码粘贴到下方');
};

// 密码登录
async function handlePasswordLogin() {
  if (!loginForm.value.username || !loginForm.value.password || !loginForm.value.captchaCallback) {
    MessagePlugin.warning('请填写完整的账号、密码和验证码');
    return;
  }
  isLoggingIn.value = true;
  try {
    const decodedString = atob(loginForm.value.captchaCallback);
    const args = decodedString.split('||');
    if (args.length !== 2) throw new Error('验证码格式错误');

    const res = await request.post(
      {
        url: '/public/login',
        baseURL: 'https://api.mefrp.com/api',
        data: {
          username: loginForm.value.username,
          password: loginForm.value.password,
          vaptchaToken: args[0],
          vaptchaServer: args[1],
        },
      },
      { withToken: false },
    );

    if (res && res.token) {
      MessagePlugin.success('登录成功');
      await handleTokenLogin(res.token);
    } else {
      MessagePlugin.error('登录失败：未获取到 Token');
    }
  } catch (e: any) {
    MessagePlugin.error('登录异常: ' + e.message);
  } finally {
    isLoggingIn.value = false;
  }
}

// Token 登录验证
async function handleTokenLogin(tokenToVerify?: string) {
  const token = tokenToVerify || loginForm.value.token;
  if (!token) {
    MessagePlugin.warning('请输入Token');
    return;
  }
  isLoggingIn.value = true;
  try {
    const res = await request.get(
      {
        url: '/auth/user/info',
        baseURL: 'https://api.mefrp.com/api',
        headers: { Authorization: `Bearer ${token}` },
      },
      { withToken: false },
    );

    if (res) {
      MessagePlugin.success('Token验证成功');
      meUserToken.value = token;
      localStorage.setItem('mefrp-user-token', token);
      userInfo.value = res;
      await initDashboardData();
    }
  } catch (e: any) {
    MessagePlugin.error('验证失败: ' + e.message);
  } finally {
    isLoggingIn.value = false;
  }
}

// 格式化流量
const formatTraffic = (kb: number) => {
  if (!kb || kb === 0) return '0 B';
  const bytes = kb * 1024;
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

// 加载仪表盘数据
async function initDashboardData() {
  loading.value = true;
  try {
    const userRes = await request.get(
      {
        url: '/auth/user/info',
        baseURL: 'https://api.mefrp.com/api',
        headers: { Authorization: `Bearer ${meUserToken.value}` },
      },
      { withToken: false },
    );

    if (userRes) {
      userInfo.value = userRes;
    } else {
      handleLogout();
      return;
    }

    const listRes = await request.get(
      {
        url: '/auth/proxy/list',
        baseURL: 'https://api.mefrp.com/api',
        headers: { Authorization: `Bearer ${meUserToken.value}` },
      },
      { withToken: false },
    );

    if (listRes) {
      const map: Record<number, string> = {};
      (listRes.nodes || []).forEach((n: any) => {
        map[n.nodeId] = n.name;
      });
      nodesMap.value = map;

      tunnels.value = listRes.proxies || [];
      if (tunnels.value.length > 0 && !selectedTunnelId.value) {
        selectedTunnelId.value = tunnels.value[0].proxyId;
      }
    }
  } catch (e: any) {
    MessagePlugin.error('数据加载失败: ' + e.message);
  } finally {
    loading.value = false;
  }
}

const isAddingTunnel = ref(false);
// 使用隧道
async function handleUseTunnel() {
  if (!currentTunnel.value) return;
  isAddingTunnel.value = true;

  try {
    const res = await request.post(
      {
        url: '/auth/proxy/config',
        baseURL: 'https://api.mefrp.com/api',
        data: { proxyId: currentTunnel.value.proxyId, format: 'toml' },
        headers: { Authorization: `Bearer ${meUserToken.value}` },
      },
      { withToken: false },
    );

    if (res && res.config) {
      await createFrpTunnel(`${currentTunnel.value.proxyName} | ${currentNodeName.value}`, res.config, 'ME Frp');
      MessagePlugin.success('配置已成功加载');
    } else {
      MessagePlugin.error('获取配置失败');
    }
  } catch (e: any) {
    MessagePlugin.error('获取配置失败: ' + e.message);
  } finally {
    isAddingTunnel.value = false;
  }
}

const handleAddTunnel = () => {
  showCreateDialog.value = true;
};

function handleLogout() {
  meUserToken.value = '';
  userInfo.value = null;
  tunnels.value = [];
  selectedTunnelId.value = null;
  localStorage.removeItem('mefrp-user-token');
  MessagePlugin.success('已退出登录');
}

async function handleRefresh() {
  await initDashboardData();
  MessagePlugin.success('数据已更新');
}

const handleSign = () => {
  const width = 800;
  const height = 600;
  const left = (window.screen.width - width) / 2;
  const top = (window.screen.height - height) / 2;
  window.open(
    `https://www.mefrp.com/3rdparty/sign?client=MSL&&token=${meUserToken.value}`,
    'MEFrpSign',
    `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`,
  );
  MessagePlugin.info('请在弹出的窗口中完成签到，完成后点击右上角刷新数据');
};

const isDeleting = ref(false);
async function handleDeleteTunnel() {
  if (!currentTunnel.value) return;
  isDeleting.value = true;

  try {
    await request.post(
      {
        url: '/auth/proxy/delete',
        baseURL: 'https://api.mefrp.com/api',
        data: { proxyId: currentTunnel.value.proxyId },
        headers: { Authorization: `Bearer ${meUserToken.value}` },
      },
      { withToken: false },
    );

    // 没报错就是成功
    MessagePlugin.success('隧道删除成功');
    selectedTunnelId.value = null;
    await initDashboardData();
  } catch (e: any) {
    MessagePlugin.error('删除失败: ' + e.message);
  } finally {
    isDeleting.value = false;
  }
}
</script>
<template>
  <div class="mx-auto pb-6 text-zinc-800 dark:text-zinc-200">

    <div v-if="meUserToken === ''" class="flex items-center justify-center min-h-[70vh] list-item-anim">
      <div class="design-card relative w-full max-w-md bg-white/80 dark:bg-zinc-800/80 backdrop-blur-xl rounded-3xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-xl p-10 text-center overflow-hidden">
        <div class="absolute -top-20 -right-20 w-60 h-60 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>
        <div class="absolute -bottom-10 -left-10 w-40 h-40 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>

        <div class="relative z-10 flex flex-col items-center">
          <div class="w-20 h-20 bg-[var(--color-primary)]/10 rounded-2xl flex items-center justify-center mb-6 shadow-sm border border-[var(--color-primary)]/20">
            <img src="https://www.mefrp.com/favicon.svg" alt="logo" size="48px" class="text-[var(--color-primary)]" />
          </div>
          <h2 class="text-2xl font-extrabold text-zinc-900 dark:text-zinc-100 !mb-2 tracking-tight">登录 ME Frp</h2>
          <p class="text-sm text-zinc-500 dark:text-zinc-400 !mb-6 font-medium">选择您的登录方式以接入内网穿透服务</p>

          <t-radio-group v-model="loginType" variant="default-filled" class="!mb-6">
            <t-radio-button value="password">账号密码登录</t-radio-button>
            <t-radio-button value="token">Token 登录</t-radio-button>
          </t-radio-group>

          <t-form v-if="loginType === 'password'" :data="loginForm" label-width="0" @submit="handlePasswordLogin" class="w-full text-left">
            <t-form-item name="username" class="!mb-4">
              <t-input v-model="loginForm.username" size="large" placeholder="请输入 ME Frp 账号" clearable class="!rounded-xl">
                <template #prefix-icon><user-circle-icon class="opacity-60" /></template>
              </t-input>
            </t-form-item>
            <t-form-item name="password" class="!mb-4">
              <t-input v-model="loginForm.password" size="large" type="password" placeholder="请输入密码" clearable class="!rounded-xl">
                <template #prefix-icon><lock-on-icon class="opacity-60" /></template>
              </t-input>
            </t-form-item>
            <t-form-item name="captchaCallback" class="!mb-6">
              <t-input v-model="loginForm.captchaCallback" size="large" placeholder="请粘贴获取到的验证码" clearable class="!rounded-xl pr-1">
                <template #prefix-icon><secured-icon class="opacity-60" /></template>
                <template #suffix>
                  <t-button variant="text" size="small" theme="primary" class="!bg-[var(--color-primary)]/10 hover:!bg-[var(--color-primary)]/20 !rounded-lg" @click="openCaptchaPage">获取验证码</t-button>
                </template>
              </t-input>
            </t-form-item>
            <t-button block theme="primary" type="submit" size="large" :loading="isLoggingIn" class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50">立即登录</t-button>
          </t-form>

          <t-form v-else :data="loginForm" label-width="0" @submit="() => handleTokenLogin()" class="w-full text-left">
            <t-form-item name="token" class="!mb-6">
              <t-input v-model="loginForm.token" size="large" type="password" placeholder="请输入 ME Frp 账户 Token" clearable class="!rounded-xl">
                <template #prefix-icon><key-icon class="opacity-60" /></template>
              </t-input>
            </t-form-item>
            <t-button block theme="primary" type="submit" size="large" :loading="isLoggingIn" class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50">验证 Token</t-button>
          </t-form>

          <div class="mt-6 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700 w-full">
            <t-button variant="text" size="small" class="text-zinc-500 hover:text-[var(--color-primary)]" @click="changeUrl('https://www.mefrp.com/register')">还没有账户？注册 ME Frp</t-button>
          </div>
        </div>
      </div>
    </div>

    <div v-else id="app-space" class="relative flex flex-col gap-6">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <div v-if="userInfo" class="design-card list-item-anim bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-5 sm:p-6" style="animation-delay: 0s;">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex flex-col">
            <h3 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">ME Frp 用户信息</h3>
          </div>
          <div class="flex items-center gap-2">
            <t-button variant="outline" theme="primary" size="small" :disabled="userInfo.todaySigned" class="!rounded-lg" :class="!userInfo.todaySigned ? 'hover:!bg-[var(--color-primary)]/10' : ''" @click="handleSign">
              {{ userInfo.todaySigned ? '今日已签到' : '每日签到' }}
            </t-button>
            <t-tag theme="primary" variant="light-outline" class="!rounded-md !font-bold">{{ userInfo.friendlyGroup }}</t-tag>
            <div class="w-px h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
            <t-popconfirm content="确认退出登录吗？" @confirm="handleLogout">
              <t-button variant="text" theme="danger" size="small" class="!rounded-lg hover:!bg-red-500/10">退出登录</t-button>
            </t-popconfirm>
          </div>
        </div>

        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1">用户昵称</div>
            <div class="flex items-center gap-2">
              <span class="text-lg font-bold text-zinc-800 dark:text-zinc-200 truncate">{{ userInfo.username }}</span>
              <t-tag v-if="userInfo.friendlyGroup !== '未实名'" theme="success" variant="light" size="small" class="!rounded !font-bold !px-1.5 border border-[var(--color-success)]/20">已实名</t-tag>
            </div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1">隧道使用情况</div>
            <div class="text-lg font-bold text-zinc-800 dark:text-zinc-200 font-mono">
              <span class="text-[var(--color-primary)]">{{ userInfo.usedProxies }}</span> / {{ userInfo.maxProxies }} <span class="text-sm font-medium text-zinc-500">条</span>
            </div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1">速率限制</div>
            <div class="text-lg font-bold text-zinc-800 dark:text-zinc-200 font-mono">{{ userInfo.outBound ? Math.floor(userInfo.outBound / 128) : 0 }} <span class="text-sm font-medium text-zinc-500">Mbps</span></div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1">剩余流量</div>
            <div class="text-[15px] font-bold text-[var(--color-success)] font-mono mt-0.5">{{ formatTraffic(userInfo.traffic) }}</div>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">

        <div class="lg:col-span-5 xl:col-span-4 design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm h-[580px]" style="animation-delay: 0.1s;">
          <div class="flex items-center justify-between p-4 sm:p-5 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
            <h3 class="text-base font-bold text-zinc-900 dark:text-zinc-100 m-0">我的隧道</h3>
            <div class="flex items-center gap-1">
              <t-button size="small" variant="text" class="!px-2 hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50" :loading="loading" @click="handleRefresh">
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
                :key="tunnel.proxyId"
                class="group flex items-center p-3 rounded-xl cursor-pointer transition-all duration-300 border"
                :class="selectedTunnelId === tunnel.proxyId ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/30 shadow-sm' : 'bg-transparent border-transparent hover:bg-zinc-50 dark:hover:bg-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600'"
                @click="selectedTunnelId = tunnel.proxyId"
              >
                <div class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0 mr-3 transition-colors"
                     :class="selectedTunnelId === tunnel.proxyId ? 'bg-[var(--color-primary)] text-white shadow-md shadow-[var(--color-primary)]/30' : 'bg-zinc-100 dark:bg-zinc-900 text-zinc-500 dark:text-zinc-400 group-hover:text-zinc-800 dark:group-hover:text-zinc-200'">
                  <server-icon size="20px" />
                </div>
                <div class="flex-1 min-w-0 mr-3">
                  <div class="font-bold text-sm truncate transition-colors" :class="selectedTunnelId === tunnel.proxyId ? 'text-[var(--color-primary)]' : 'text-zinc-800 dark:text-zinc-200'">{{ tunnel.proxyName }}</div>
                  <div class="text-[11px] text-zinc-500 dark:text-zinc-400 truncate mt-0.5">{{ nodesMap[tunnel.nodeId] || `Node ${tunnel.nodeId}` }}</div>
                </div>
                <div class="shrink-0">
                  <t-tag v-if="tunnel.isOnline" theme="success" variant="light" size="small" class="!rounded !font-bold !px-1.5">在线</t-tag>
                  <t-tag v-else theme="default" variant="light" size="small" class="!rounded !font-bold !px-1.5 !text-zinc-500">离线</t-tag>
                </div>
              </div>
            </div>

            <div v-else class="h-full flex flex-col items-center justify-center opacity-60">
              <server-icon size="32px" class="text-zinc-400 mb-2" />
              <span class="text-sm text-zinc-500 font-medium">暂无隧道，请先新建</span>
            </div>
          </div>
        </div>

        <div class="lg:col-span-7 xl:col-span-8 design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm h-[580px]" style="animation-delay: 0.2s;">

          <template v-if="currentTunnel">
            <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
              <div class="flex flex-col min-w-0">
                <h3 class="text-xl font-extrabold text-zinc-900 dark:text-zinc-100 m-0 truncate">{{ currentTunnel.proxyName }}</h3>
                <p class="text-xs text-zinc-500 dark:text-zinc-400 mt-1 truncate font-mono bg-zinc-100 dark:bg-zinc-800/50 w-max px-2 py-0.5 rounded">ID: {{ currentTunnel.proxyId }}</p>
              </div>
              <div class="shrink-0">
                <t-popconfirm content="确认删除此隧道吗？将无法恢复！" theme="danger" placement="bottom-right" @confirm="handleDeleteTunnel">
                  <t-button theme="danger" class="!rounded-lg hover:!bg-red-500 hover:!text-white transition-colors" :loading="isDeleting">
                    <template #icon><t-icon name="delete" /></template>
                    删除隧道
                  </t-button>
                </t-popconfirm>
              </div>
            </div>

            <div class="flex-1 overflow-y-auto custom-scrollbar p-5 sm:p-6">
              <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-2 gap-4">
                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1.5">所在节点</span>
                  <span class="text-sm font-bold text-zinc-800 dark:text-zinc-200 truncate" :title="currentNodeName">{{ currentNodeName }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-zinc-400 dark:text-zinc-500 uppercase tracking-widest mb-1.5">本地地址</span>
                  <span class="text-sm font-mono font-bold text-zinc-800 dark:text-zinc-200">{{ currentTunnel.localIp }}:{{ currentTunnel.localPort }}</span>
                </div>

                <div class="p-4 bg-emerald-50/50 dark:bg-emerald-900/20 rounded-xl border border-emerald-200/50 dark:border-emerald-800/30 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-emerald-600/80 dark:text-emerald-500/80 uppercase tracking-widest mb-1.5">远程公网端口</span>
                  <span class="text-lg font-mono font-extrabold text-emerald-600 dark:text-emerald-400">{{ currentTunnel.remotePort }}</span>
                </div>

                <div class="p-4 rounded-xl flex flex-col justify-center border transition-colors"
                     :class="currentTunnel.isOnline ? 'bg-emerald-50/50 dark:bg-emerald-900/10 border-emerald-200/50 dark:border-emerald-800/30' : 'bg-zinc-50/80 dark:bg-zinc-900/50 border-zinc-200/50 dark:border-zinc-700/50'">
                  <span class="text-[11px] font-extrabold uppercase tracking-widest mb-1.5"
                        :class="currentTunnel.isOnline ? 'text-emerald-600/80 dark:text-emerald-500/80' : 'text-zinc-400 dark:text-zinc-500'">当前状态</span>
                  <div class="flex items-center gap-2">
                    <span v-if="currentTunnel.isOnline" class="w-2 h-2 rounded-full bg-[var(--color-success)] animate-pulse"></span>
                    <span class="text-sm font-bold" :class="currentTunnel.isOnline ? 'text-[var(--color-success)]' : 'text-zinc-500'">
                      {{ currentTunnel.isOnline ? '节点在线' : '离线' }}
                    </span>
                  </div>
                </div>
              </div>

              <div class="mt-8">
                <t-button theme="primary" size="large" :loading="isAddingTunnel" block class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow text-base" @click="handleUseTunnel">
                  <template #icon><play-circle-icon /></template>
                  启动此隧道映射
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
              <p class="text-sm text-zinc-500">请在左侧列表中选择一个隧道以查看详细信息和连接参数</p>
            </div>
          </template>

        </div>
      </div>
    </div>

    <create-tunnel-dialog v-if="showCreateDialog" v-model:visible="showCreateDialog" :token="meUserToken" @success="handleCreateSuccess" />
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
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

/* 滚动条混入 */
.custom-scrollbar {
  .scrollbar-mixin();
}

/* 针对 Loading 遮罩的细节优化 */
:deep(.t-loading__overlay) {
@apply !rounded-2xl !bg-white/50 dark:!bg-zinc-900/50 backdrop-blur-sm;
}
</style>
