<script setup lang="ts">
import {
  ServerIcon,
  CloudIcon,
  AddIcon,
  PlayCircleIcon,
  RefreshIcon,
  KeyIcon,
} from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { onMounted, ref, computed } from 'vue';
import { request } from '@/utils/request';
import { MessagePlugin } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import CreateTunnelDialog from './components/CreateTunnelDialog.vue';

const showCreateDialog = ref(false);
const sakuraToken = ref('');

// 数据状态
const loading = ref(false);
const userInfo = ref<any>(null);
const userLevel = ref<number>(0);
const tunnels = ref<any[]>([]);
const nodesMap = ref<Record<number, string>>({}); // 缓存节点ID -> 节点名称
const selectedTunnelId = ref<number | null>(null);

// 登录相关状态
const loginForm = ref({
  token: '',
});
const isLoggingIn = ref(false);

const handleCreateSuccess = () => {
  initDashboardData();
};

const currentTunnel = computed(() => {
  return tunnels.value.find((t) => t.id === selectedTunnelId.value) || null;
});

const currentNodeName = computed(() => {
  if (!currentTunnel.value) return '';
  return nodesMap.value[currentTunnel.value.node] || `节点 (${currentTunnel.value.node})`;
});

onMounted(() => {
  const token = localStorage.getItem('sakurafrp-user-token');
  if (token) {
    sakuraToken.value = token;
    initDashboardData();
  }
});

// Token 登录验证
async function handleTokenLogin(tokenToVerify?: string) {
  const token = tokenToVerify || loginForm.value.token;
  if (!token) {
    MessagePlugin.warning('请输入 Token');
    return;
  }
  isLoggingIn.value = true;
  try {
    const res = await request.get(
      {
        url: `/user/info?token=${token}`,
        baseURL: 'https://api.natfrp.com/v4',
      },
      { withToken: false },
    );

    if (res && res.name) {
      MessagePlugin.success('Token验证成功');
      sakuraToken.value = token;
      localStorage.setItem('sakurafrp-user-token', token);
      userInfo.value = res;
      userLevel.value = parseInt(res.group?.level || '0');
      await initDashboardData();
    } else {
      MessagePlugin.error('登录失败：未获取到有效的用户信息');
    }
  } catch (e: any) {
    MessagePlugin.error('验证失败: ' + e.message);
  } finally {
    isLoggingIn.value = false;
  }
}

// 加载仪表盘数据
async function initDashboardData() {
  loading.value = true;
  try {
    // 获取用户信息
    const userRes = await request.get(
      {
        url: `/user/info?token=${sakuraToken.value}`,
        baseURL: 'https://api.natfrp.com/v4',
      },
      { withToken: false },
    );

    if (userRes && userRes.name) {
      userInfo.value = userRes;
      userLevel.value = parseInt(userRes.group?.level || '0');
    } else {
      handleLogout();
      return;
    }

    // 获取节点列表，用于名称映射
    const nodesRes = await request.get(
      {
        url: `/nodes?token=${sakuraToken.value}`,
        baseURL: 'https://api.natfrp.com/v4',
      },
      { withToken: false },
    );

    if (nodesRes) {
      const map: Record<number, string> = {};
      Object.entries(nodesRes).forEach(([id, data]: [string, any]) => {
        map[parseInt(id)] = data.name;
      });
      nodesMap.value = map;
    }

    // 获取隧道列表
    const tunnelsRes = await request.get(
      {
        url: `/tunnels?token=${sakuraToken.value}`,
        baseURL: 'https://api.natfrp.com/v4',
      },
      { withToken: false },
    );

    if (Array.isArray(tunnelsRes)) {
      tunnels.value = tunnelsRes || [];
      if (tunnels.value.length > 0 && !selectedTunnelId.value) {
        selectedTunnelId.value = tunnels.value[0].id;
      }
    }
  } catch (e: any) {
    MessagePlugin.error('数据加载失败: ' + e.message);
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
    const res = await request.post(
      {
        url: '/tunnel/config',
        baseURL: 'https://api.natfrp.com/v4',
        headers: {
          Authorization: `Bearer ${sakuraToken.value}`,
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        data: `query=${currentTunnel.value.id}&frpc=0.52.0`,
      },
      { withToken: false },
    );

    if (res && typeof res === 'string') {
      await createFrpTunnel(`${currentTunnel.value.name} | ${currentNodeName.value}`, res, 'Sakura Frp');
      MessagePlugin.success('配置文件已成功加载');
    } else {
      MessagePlugin.error('获取配置失败：内容为空或格式异常');
    }
  } catch (e: any) {
    const errorMsg = e.response?.data?.msg || e.msg || e.message || '未知错误';
    MessagePlugin.error(`获取配置异常: ${errorMsg}`);
  } finally {
    isAddingTunnel.value = false;
  }
}

const handleAddTunnel = () => {
  showCreateDialog.value = true;
};

function handleLogout() {
  sakuraToken.value = '';
  userInfo.value = null;
  tunnels.value = [];
  selectedTunnelId.value = null;
  localStorage.removeItem('sakurafrp-user-token');
  MessagePlugin.success('已退出登录');
}

async function handleRefresh() {
  await initDashboardData();
  MessagePlugin.success('数据已更新');
}

const isDeleting = ref(false);
async function handleDeleteTunnel() {
  if (!currentTunnel.value) return;
  isDeleting.value = true;

  try {
    await request.post(
      {
        url: '/tunnel/delete',
        baseURL: 'https://api.natfrp.com/v4',
        headers: { Authorization: `Bearer ${sakuraToken.value}`,'Content-Type': 'application/x-www-form-urlencoded' },
        data: `ids=${currentTunnel.value.id}`,
      },
      { withToken: false },
    );

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
  <div class="mx-auto pb-6 text-[var(--td-text-color-primary)]">

    <div v-if="sakuraToken === ''" class="flex items-center justify-center min-h-[70vh] list-item-anim">
      <div class="design-card relative w-full max-w-md bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-xl p-10 text-center overflow-hidden">
        <div class="absolute -top-20 -right-20 w-60 h-60 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>
        <div class="absolute -bottom-10 -left-10 w-40 h-40 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>

        <div class="relative z-10 flex flex-col items-center">
          <div class="w-20 h-20 bg-[var(--color-primary)]/10 rounded-2xl flex items-center justify-center mb-6 shadow-sm border border-[var(--color-primary)]/20">
            <img src="https://www.natfrp.com/favicon.ico" alt="logo" class="text-[var(--color-primary)]" />
          </div>
          <h2 class="text-2xl font-extrabold text-[var(--td-text-color-primary)] !mb-2 tracking-tight">登录 SakuraFrp</h2>
          <p class="text-sm text-[var(--td-text-color-secondary)] !mb-8 font-medium">使用您的访问令牌 (Token) 连接服务</p>

          <t-form :data="loginForm" label-width="0" @submit="() => handleTokenLogin()" class="w-full text-left">
            <t-form-item name="token" class="!mb-6">
              <t-input v-model="loginForm.token" size="large" type="password" placeholder="请输入 SakuraFrp 访问 Token" clearable class="!rounded-xl">
                <template #prefix-icon><key-icon class="opacity-60" /></template>
              </t-input>
            </t-form-item>
            <t-button block theme="primary" type="submit" size="large" :loading="isLoggingIn" class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50">立即验证 Token</t-button>
          </t-form>

          <div class="mt-8 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700 w-full">
            <t-button variant="text" size="small" class="text-zinc-500 hover:text-[var(--color-primary)]" @click="changeUrl('https://www.natfrp.com/user/')">获取 SakuraFrp 账号Token</t-button>
          </div>
        </div>
      </div>
    </div>

    <div v-else id="app-space" class="relative flex flex-col gap-6">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <div v-if="userInfo" class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5 sm:p-6" style="animation-delay: 0s;">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex flex-col">
            <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">SakuraFrp 账户信息</h3>
          </div>
          <div class="flex items-center gap-2">
            <t-tag theme="primary" variant="light-outline" class="!rounded-md !font-bold">{{ userInfo.group?.name || '未知分组' }}</t-tag>
            <div class="w-px h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
            <t-popconfirm content="确认断开 SakuraFrp 的连接吗？" @confirm="handleLogout">
              <t-button variant="text" theme="danger" size="small" class="!rounded-lg hover:!bg-red-500/10">退出登录</t-button>
            </t-popconfirm>
          </div>
        </div>

        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">用户名称</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] truncate">{{ userInfo.name }}</div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">当前隧道数</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">
              <span class="text-[var(--color-primary)]">{{ tunnels.length }}</span> <span class="text-sm font-medium text-zinc-500">条</span>
            </div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">限速</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">{{ userInfo.speed || '无限制' }}</div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">VIP 等级</div>
            <div class="text-[15px] font-bold text-[var(--color-success)] font-mono mt-0.5">Level {{ userLevel }}</div>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">

        <div class="lg:col-span-5 xl:col-span-4 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.1s;">
          <div class="flex items-center justify-between p-4 sm:p-5 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
            <h3 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">我的隧道</h3>
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
                :key="tunnel.id"
                class="group flex items-center p-3 rounded-xl cursor-pointer transition-all duration-300 border"
                :class="selectedTunnelId === tunnel.id ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/30 shadow-sm' : 'bg-transparent border-transparent hover:bg-zinc-50 dark:hover:bg-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600'"
                @click="selectedTunnelId = tunnel.id"
              >
                <div class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0 mr-3 transition-colors"
                     :class="selectedTunnelId === tunnel.id ? 'bg-[var(--color-primary)] text-white shadow-md shadow-[var(--color-primary)]/30' : 'bg-zinc-100 dark:bg-zinc-900 text-[var(--td-text-color-secondary)] group-hover:text-zinc-800 dark:group-hover:text-zinc-200'">
                  <server-icon size="20px" />
                </div>
                <div class="flex-1 min-w-0 mr-3">
                  <div class="font-bold text-sm truncate transition-colors" :class="selectedTunnelId === tunnel.id ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-primary)]'">{{ tunnel.name }}</div>
                  <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">{{ nodesMap[tunnel.node] || `Node ${tunnel.node}` }}</div>
                </div>
                <div class="shrink-0">
                  <t-tag v-if="tunnel.online" theme="success" variant="light" size="small" class="!rounded !font-bold !px-1.5">在线</t-tag>
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

        <div class="lg:col-span-7 xl:col-span-8 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.2s;">

          <template v-if="currentTunnel">
            <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
              <div class="flex flex-col min-w-0">
                <h3 class="text-xl font-extrabold text-[var(--td-text-color-primary)] m-0 truncate">{{ currentTunnel.name }}</h3>
                <p class="text-xs text-[var(--td-text-color-secondary)] mt-1 truncate font-mono bg-zinc-100 dark:bg-zinc-800/50 w-max px-2 py-0.5 rounded">ID: {{ currentTunnel.id }}</p>
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
                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">所在节点</span>
                  <span class="text-sm font-bold text-[var(--td-text-color-primary)] truncate" :title="currentNodeName">{{ currentNodeName }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">本地地址</span>
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]">{{ currentTunnel.local_ip }}:{{ currentTunnel.local_port }}</span>
                </div>

                <div class="p-4 bg-emerald-50/50 dark:bg-emerald-900/20 rounded-xl border border-emerald-200/50 dark:border-emerald-800/30 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-emerald-600/80 dark:text-emerald-500/80 uppercase tracking-widest mb-1.5">远程信息 (端口/域名)</span>
                  <span class="text-lg font-mono font-extrabold text-emerald-600 dark:text-emerald-400">{{ currentTunnel.remote }}</span>
                </div>

                <div class="p-4 rounded-xl flex flex-col justify-center border transition-colors"
                     :class="currentTunnel.online ? 'bg-emerald-50/50 dark:bg-emerald-900/10 border-emerald-200/50 dark:border-emerald-800/30' : 'bg-zinc-50/80 dark:bg-zinc-900/50 border-[var(--td-component-border)]'">
                  <span class="text-[11px] font-extrabold uppercase tracking-widest mb-1.5"
                        :class="currentTunnel.online ? 'text-emerald-600/80 dark:text-emerald-500/80' : 'text-[var(--td-text-color-secondary)]'">当前状态</span>
                  <div class="flex items-center gap-2">
                    <span v-if="currentTunnel.online" class="w-2 h-2 rounded-full bg-[var(--color-success)] animate-pulse"></span>
                    <span class="text-sm font-bold" :class="currentTunnel.online ? 'text-[var(--color-success)]' : 'text-zinc-500'">
                      {{ currentTunnel.online ? '节点在线' : '离线' }}
                    </span>
                  </div>
                </div>
              </div>

              <div class="mt-8">
                <t-button theme="primary" size="large" :loading="isAddingTunnel" block class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow text-base" @click="handleUseTunnel">
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

    <create-tunnel-dialog v-if="showCreateDialog" v-model:visible="showCreateDialog" :token="sakuraToken" :userLevel="userLevel" @success="handleCreateSuccess" />
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
@apply !rounded-2xl !bg-white/50 dark:!bg-zinc-900/50;
  animation: smoothLoadingGlass 0.3s cubic-bezier(0.2, 0.8, 0.2, 1) forwards !important;
}
</style>
