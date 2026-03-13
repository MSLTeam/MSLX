<script setup lang="ts">
import {
  UserCircleIcon,
  UserAddIcon,
  ServerIcon,
  CloudIcon,
  AddIcon,
  PlayCircleIcon,
  RefreshIcon,
} from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { onMounted, onUnmounted, ref, computed } from 'vue';
import { request } from '@/utils/request';
import { formatTime, generateRandomString } from '@/utils/tools';
import { MessagePlugin } from 'tdesign-vue-next';
import { openLoginPopup } from '@/utils/popup';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import CreateTunnelDialog from './components/CreateTunnelDialog.vue';
import DomainManagerDialog from './components/DomainManagerDialog.vue';

const showDomainDialog = ref(false);
const showCreateDialog = ref(false);

// 创建成功后的回调
const handleCreateSuccess = () => {
  initDashboardData(); // 刷新列表
};

interface UserInfo {
  uid: number;
  name: string;
  user_group_name: string;
  outdated: number;
  maxTunnelCount: number;
  boundLimit: number;
  realNameStatus: boolean;
}

interface Tunnel {
  id: number;
  uid: number;
  node_id: number;
  name: string;
  local_ip: string;
  local_port: number;
  remote_port: number;
  type: string;
  remarks: string;
  status: number;
  today_traffic: number;
  total_traffic: number;
}

interface NodeInfo {
  id: number;
  node: string;
  remarks: string;
}

const clientId = ref('tKYvKk48Sq5kGAy12IJQxLEKhXx');
const popupWindow = ref<Window | null>(null);
const mslUserToken = ref('');

// 数据状态
const loading = ref(false);
const userInfo = ref<UserInfo | null>(null);
const tunnels = ref<Tunnel[]>([]);
const nodesMap = ref<Record<number, string>>({}); // 缓存节点ID -> 节点名称
const selectedTunnelId = ref<number | null>(null); // 当前选中的隧道ID

// 计算属性：当前选中的隧道对象
const currentTunnel = computed(() => {
  return tunnels.value.find((t) => t.id === selectedTunnelId.value) || null;
});

// 计算属性：当前选中隧道的节点名称
const currentNodeName = computed(() => {
  if (!currentTunnel.value) return '';
  return nodesMap.value[currentTunnel.value.node_id] || `未知节点 (${currentTunnel.value.node_id})`;
});

onMounted(() => {
  const token = localStorage.getItem('msl-user-token');
  if (token) {
    mslUserToken.value = token;
    initDashboardData();
  }
});

onUnmounted(() => {
  if (popupWindow.value && !popupWindow.value.closed) {
    popupWindow.value.close();
  }
});

// 登录相关函数
async function jumpLogin() {
  MessagePlugin.info('正在跳转至MSL用户中心登录...');
  try {
    const csrf = generateRandomString(32);
    const res = await request.post({
      url: '/api/oauth/createAppLogin',
      baseURL: 'https://user.mslmc.net',
      data: { appid: clientId.value, csrf: csrf },
    });
    if (res.data.ssid) {
      popupWindow.value = openLoginPopup(res.data.url, '登录到您的MSL账户', 600, 600);
      setTimeout(() => pollLoginStatus(csrf, res.data.ssid), 1000);
    } else {
      MessagePlugin.error(res.msg);
    }
  } catch (e: any) {
    MessagePlugin.error(e.message);
  }
}

async function pollLoginStatus(csrf: string, ssid: string) {
  if (popupWindow.value?.closed) return;
  try {
    const res = await request.get({
      url: '/api/oauth/appLogin',
      baseURL: 'https://user.mslmc.net',
      params: { csrf, ssid },
    });
    if (res.data.token) {
      if (popupWindow.value) popupWindow.value.close();
      MessagePlugin.success('登录成功');
      mslUserToken.value = res.data.token;
      localStorage.setItem('msl-user-token', res.data.token);

      // 登录成功后，初始化数据
      initDashboardData();
      return;
    }
  } catch (e) {
    MessagePlugin.error('登录失败！' + e.message);
    return;
  }
  setTimeout(() => pollLoginStatus(csrf, ssid), 1000);
}

// 格式化流量
const formatBytes = (bytes: number) => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

// 加载所有数据
async function initDashboardData() {
  loading.value = true;
  try {
    // 并行获取 用户信息和节点列表
    const [userRes, nodeRes] = await Promise.all([
      request.get({
        url: '/api/frp/userInfo',
        baseURL: 'https://user.mslmc.net',
        headers: { Authorization: `Bearer ${mslUserToken.value}` },
      }),
      request.get({
        url: '/api/frp/nodeList',
        baseURL: 'https://user.mslmc.net',
        headers: { Authorization: `Bearer ${mslUserToken.value}` },
      }),
    ]);

    if (userRes.code === 200) {
      userInfo.value = userRes.data;
    } else {
      MessagePlugin.warning('登录已过期，请重新登录～');
      mslUserToken.value = '';
      localStorage.removeItem('msl-user-token');
      return;
    }

    if (nodeRes.code === 200) {
      // 节点id和名字映射
      const map: Record<number, string> = {};
      nodeRes.data.forEach((n: NodeInfo) => {
        map[n.id] = n.node;
      });
      nodesMap.value = map;
    }

    const tunnelRes = await request.get({
      url: '/api/frp/getTunnelList',
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${mslUserToken.value}` },
    });

    if (tunnelRes.code === 200) {
      tunnels.value = tunnelRes.data;
      if (tunnels.value.length > 0) {
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
// 获取隧道配置并添加
async function handleUseTunnel() {
  if (!currentTunnel.value) return;
  isAddingTunnel.value = true;

  try {
    const res = await request.get({
      url: '/api/frp/getTunnelConfig',
      baseURL: 'https://user.mslmc.net',
      params: { id: currentTunnel.value.id },
      headers: { Authorization: `Bearer ${mslUserToken.value}` },
    });

    if (res.code === 200) {
      await createFrpTunnel(`${currentTunnel.value.name} | ${currentNodeName.value}`, res.data, 'MSLFrp');
    } else {
      MessagePlugin.error(res.msg);
    }
  } catch (e: any) {
    MessagePlugin.error('获取配置失败: ' + e.message);
  }
  isAddingTunnel.value = false;
}

const handleAddTunnel = () => {
  showCreateDialog.value = true;
};

// 退出登录
async function handleLogout() {
  try {
    await request.get({
      url: '/api/user/logout',
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${mslUserToken.value}` },
    });

    mslUserToken.value = '';
    userInfo.value = null;
    tunnels.value = [];
    localStorage.removeItem('msl-user-token');

    MessagePlugin.success('已退出登录');
  } catch (e: any) {
    MessagePlugin.error('退出失败: ' + e.message);
  }
}

async function handleRefresh() {
  await initDashboardData();
  MessagePlugin.success('数据已更新');
}

// 删除隧道逻辑
const isDeleting = ref(false);

async function handleDeleteTunnel() {
  if (!currentTunnel.value) return;
  isDeleting.value = true;

  try {
    const res = await request.post({
      url: '/api/frp/deleteTunnel',
      baseURL: 'https://user.mslmc.net',
      data: { id: currentTunnel.value.id },
      headers: { Authorization: `Bearer ${mslUserToken.value}` },
    });

    if (res.code === 200) {
      MessagePlugin.success('隧道删除成功');
      selectedTunnelId.value = null; // 清空当前选中
      await initDashboardData(); // 重新刷新列表
    } else {
      MessagePlugin.error(res.msg || '删除失败');
    }
  } catch (e: any) {
    MessagePlugin.error('操作失败: ' + e.message);
  } finally {
    isDeleting.value = false;
  }
}
</script>

<template>
  <div class="mx-auto pb-6 text-[var(--td-text-color-primary)]">

    <div v-if="mslUserToken === ''" class="flex items-center justify-center min-h-[70vh] list-item-anim">
      <div class="design-card relative w-full max-w-md bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-xl p-10 text-center overflow-hidden">
        <div class="absolute -top-20 -right-20 w-60 h-60 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>
        <div class="absolute -bottom-10 -left-10 w-40 h-40 bg-[var(--color-primary)]/10 rounded-full blur-3xl pointer-events-none"></div>

        <div class="relative z-10 flex flex-col items-center">
          <div class="w-20 h-20 rounded-2xl flex items-center justify-center mb-6 shadow-sm">
            <img src="https://user.mslmc.net/assets/png/msl-user-msl-user-logo-512-transparent-BjXu1GPW.png" alt="msl-user-logo" class="text-[var(--color-primary)]" />
          </div>
          <h2 class="text-2xl font-extrabold text-[var(--td-text-color-primary)] !mb-2 tracking-tight">欢迎登录 MSLFrp</h2>
          <p class="text-sm text-[var(--td-text-color-secondary)] !mb-8 font-medium">登录您的 MSL 账户以使用内网穿透服务</p>

          <div class="w-full flex flex-col gap-4">
            <t-button block theme="primary" size="large" class="!rounded-xl !h-12 !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50" @click="jumpLogin">
              <template #icon><user-circle-icon /></template>
              跳转用户中心登录
            </t-button>
            <t-button theme="default" variant="outline" block size="large" class="!rounded-xl !h-12 !font-bold !bg-white/50 dark:!bg-zinc-900/50 !border-zinc-200 dark:!border-zinc-700 hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)]/50 !ml-0" @click="changeUrl('https://user.mslmc.net/register')">
              <template #icon><user-add-icon /></template>
              注册 MSL 账户
            </t-button>
          </div>
        </div>
      </div>
    </div>

    <div v-else id="app-space" class="relative flex flex-col gap-6">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <div v-if="userInfo" class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80  rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5 sm:p-6" style="animation-delay: 0s;">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex flex-col">
            <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">MSLFrp 用户信息</h3>
          </div>
          <div class="flex items-center gap-2">
            <t-button variant="outline" theme="success" size="small" class="!rounded-lg hover:!bg-[var(--color-success)]/10" @click="changeUrl('https://user.mslmc.net/store/buy')">订阅会员服务</t-button>
            <t-tag theme="primary" variant="light-outline" class="!rounded-md !font-bold">{{ userInfo.user_group_name }}</t-tag>
            <div class="w-px h-4 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
            <t-popconfirm content="确认退出登录吗？" @confirm="handleLogout">
              <t-button variant="text" theme="danger" size="small" class="!rounded-lg hover:!bg-red-500/10">退出登录</t-button>
            </t-popconfirm>
          </div>
        </div>

        <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">用户昵称</div>
            <div class="flex items-center gap-2">
              <span class="text-lg font-bold text-[var(--td-text-color-primary)] truncate">{{ userInfo.name }}</span>
              <t-tag :theme="userInfo.realNameStatus ? 'success' : 'warning'" variant="light" size="small" class="!rounded cursor-pointer !font-bold !px-1.5" @click="changeUrl('https://user.mslmc.net/user/profile')">
                {{ userInfo.realNameStatus ? '已实名' : '未实名' }}
              </t-tag>
            </div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">隧道限额</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">{{ userInfo.maxTunnelCount }} <span class="text-sm font-medium text-zinc-500">条</span></div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">速率限制</div>
            <div class="text-lg font-bold text-[var(--td-text-color-primary)] font-mono">{{ (userInfo.boundLimit / 1024) * 8 }} <span class="text-sm font-medium text-zinc-500">Mbps</span></div>
          </div>

          <div class="p-4 rounded-xl bg-zinc-50/80 dark:bg-zinc-900/50 border border-zinc-100 dark:border-zinc-800 transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <div class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1">会员到期时间</div>
            <div class="text-[15px] font-bold text-[var(--td-text-color-primary)] font-mono mt-0.5">
              {{ userInfo.outdated === 3749682420 ? '长期有效' : formatTime(userInfo.outdated) }}
            </div>
          </div>
        </div>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">

        <div class="lg:col-span-5 xl:col-span-4 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80  rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.1s;">
          <div class="flex items-center justify-between p-4 sm:p-5 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
            <h3 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">我的隧道</h3>
            <div class="flex items-center gap-1">
              <t-button size="small" variant="text" class="!px-2 hover:!bg-zinc-100 dark:hover:!bg-zinc-700/50" :loading="loading" @click="handleRefresh">
                <template #icon><refresh-icon /></template>刷新
              </t-button>
              <t-button size="small" variant="text" class="!px-2 hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)]" @click="showDomainDialog = true">
                <template #icon><cloud-icon /></template>子域名
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
                  <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">{{ nodesMap[tunnel.node_id] || `Node ${tunnel.node_id}` }}</div>
                </div>
                <div class="shrink-0">
                  <t-tag v-if="tunnel.status === 1" theme="success" variant="light" size="small" class="!rounded !font-bold !px-1.5">在线</t-tag>
                  <t-tag v-else theme="default" variant="light" size="small" class="!rounded !font-bold !px-1.5 !text-zinc-500">未启动</t-tag>
                </div>
              </div>
            </div>

            <div v-else class="h-full flex flex-col items-center justify-center opacity-60">
              <server-icon size="32px" class="text-zinc-400 mb-2" />
              <span class="text-sm text-zinc-500 font-medium">暂无隧道，请先新建</span>
            </div>
          </div>
        </div>

        <div class="lg:col-span-7 xl:col-span-8 design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80  rounded-2xl border border-[var(--td-component-border)] shadow-sm h-[580px]" style="animation-delay: 0.2s;">

          <template v-if="currentTunnel">
            <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 shrink-0">
              <div class="flex flex-col min-w-0">
                <h3 class="text-xl font-extrabold text-[var(--td-text-color-primary)] m-0 truncate">{{ currentTunnel.name }}</h3>
                <p class="text-xs text-[var(--td-text-color-secondary)] mt-1 truncate">{{ currentTunnel.remarks || '暂无备注' }}</p>
              </div>
              <div class="shrink-0">
                <t-popconfirm content="确认删除此隧道吗？将无法恢复！" theme="danger" placement="bottom-right" @confirm="handleDeleteTunnel">
                  <t-button theme="danger"  class="!rounded-lg hover:!bg-red-500 hover:!text-white transition-colors" :loading="isDeleting">
                    <template #icon><t-icon name="delete" /></template>
                    删除隧道
                  </t-button>
                </t-popconfirm>
              </div>
            </div>

            <div class="flex-1 overflow-y-auto custom-scrollbar p-5 sm:p-6">
              <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">所在节点</span>
                  <span class="text-sm font-bold text-[var(--td-text-color-primary)] truncate" :title="currentNodeName">{{ currentNodeName }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">协议类型</span>
                  <span class="text-sm font-bold text-[var(--color-primary)] uppercase tracking-wide">{{ currentTunnel.type }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">本地地址</span>
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]">{{ currentTunnel.local_ip }}:{{ currentTunnel.local_port }}</span>
                </div>

                <div class="p-4 bg-emerald-50/50 dark:bg-emerald-900/20 rounded-xl border border-emerald-200/50 dark:border-emerald-800/30 flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-emerald-600/80 dark:text-emerald-500/80 uppercase tracking-widest mb-1.5">远程公网端口</span>
                  <span class="text-lg font-mono font-extrabold text-emerald-600 dark:text-emerald-400">{{ currentTunnel.remote_port }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">今日流量</span>
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]">{{ formatBytes(currentTunnel.today_traffic * 1024 * 1024) }}</span>
                </div>

                <div class="p-4 bg-zinc-50/80 dark:bg-zinc-900/50 rounded-xl border border-[var(--td-component-border)] flex flex-col justify-center">
                  <span class="text-[11px] font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest mb-1.5">总流量</span>
                  <span class="text-sm font-mono font-bold text-[var(--td-text-color-primary)]">{{ formatBytes(currentTunnel.total_traffic * 1024 * 1024) }}</span>
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
              <p class="text-sm text-zinc-500">请在左侧列表中选择一个隧道以查看详细信息和连接参数</p>
            </div>
          </template>

        </div>
      </div>
    </div>

    <create-tunnel-dialog v-if="showCreateDialog" v-model:visible="showCreateDialog" :token="mslUserToken" @success="handleCreateSuccess" />
    <domain-manager-dialog v-if="showDomainDialog" v-model:visible="showDomainDialog" :token="mslUserToken" :tunnels="tunnels" />
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
