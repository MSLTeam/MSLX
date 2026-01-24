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
      await createFrpTunnel(currentTunnel.value.name, res.data, 'Index'); // 错误会抛出 这个函数会自动跳转+成功提示
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
  <div class="page-container">
    <div v-if="mslUserToken === ''" class="login-container">
      <div class="login-card">
        <div class="icon-wrapper">
          <user-circle-icon size="48px" style="color: var(--td-brand-color)" />
        </div>
        <h2 class="title">欢迎登录 MSLFrp</h2>
        <p class="subtitle">登录您的MSL账户以使用 MSLFrp 服务</p>
        <t-space direction="vertical" size="medium" style="width: 100%">
          <t-button block theme="primary" size="large" @click="jumpLogin">
            <template #icon><user-circle-icon /></template>
            跳转用户中心登录
          </t-button>
          <t-button
            theme="default"
            variant="outline"
            block
            size="large"
            @click="changeUrl('https://user.mslmc.net/register')"
          >
            <template #icon><user-add-icon /></template>
            注册 MSL 账户
          </t-button>
        </t-space>
      </div>
    </div>

    <div v-else class="dashboard-container">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <t-space id="app-space" direction="vertical" size="large" style="width: 100%">
        <t-card v-if="userInfo" :bordered="false" title="MSLFrp 用户信息" class="info-card">
          <template #actions>
            <t-space size="small">
              <t-tag theme="primary" variant="light">{{ userInfo.user_group_name }}</t-tag>
              <t-popconfirm content="确认退出登录吗？" @confirm="handleLogout">
                <t-button variant="text" theme="danger" size="small"> 退出登录 </t-button>
              </t-popconfirm>
            </t-space>
          </template>
          <t-row :gutter="[16, 16]">
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">用户昵称</div>
                <div class="value">{{ userInfo.name }}</div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">隧道限额</div>
                <div class="value">{{ userInfo.maxTunnelCount }} 条</div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">速率限制</div>
                <div class="value">{{ (userInfo.boundLimit / 1024) * 8 }} Mbps</div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">到期时间</div>
                <div class="value date-text">{{ formatTime(userInfo.outdated) }}</div>
              </div>
            </t-col>
          </t-row>
        </t-card>

        <t-row :gutter="[16, 16]">
          <t-col :xs="12" :md="5" :lg="4">
            <t-card :bordered="false" class="full-height-card">
              <template #title>
                <div class="list-header">
                  <span>我的隧道</span>
                  <t-space size="4px">
                    <t-button
                      style="margin-left: 3px"
                      size="small"
                      variant="text"
                      :loading="loading"
                      @click="handleRefresh"
                    >
                      <template #icon><refresh-icon /></template>
                      刷新
                    </t-button>
                    <t-button size="small" variant="text" @click="handleAddTunnel">
                      <template #icon><add-icon /></template>
                      新建
                    </t-button>
                  </t-space>
                </div>
              </template>

              <div class="tunnel-list">
                <div
                  v-for="tunnel in tunnels"
                  :key="tunnel.id"
                  class="tunnel-item"
                  :class="{ active: selectedTunnelId === tunnel.id }"
                  @click="selectedTunnelId = tunnel.id"
                >
                  <div class="item-icon">
                    <server-icon />
                  </div>
                  <div class="item-content">
                    <div class="item-title">{{ tunnel.name }}</div>
                    <div class="item-subtitle">{{ nodesMap[tunnel.node_id] || `Node ${tunnel.node_id}` }}</div>
                  </div>
                  <div class="item-status">
                    <t-tag v-if="tunnel.status === 1" theme="success" variant="light" size="small">在线</t-tag>
                    <t-tag v-else theme="default" variant="light" size="small">未启动</t-tag>
                  </div>
                </div>

                <div v-if="tunnels.length === 0" class="empty-state">暂无隧道</div>
              </div>
            </t-card>
          </t-col>

          <t-col :xs="12" :md="7" :lg="8">
            <t-card :bordered="false" class="full-height-card">
              <template #title>
                <span>隧道详情</span>
              </template>

              <template v-if="currentTunnel" #actions>
                <t-popconfirm
                  content="确认删除此隧道吗？"
                  theme="danger"
                  placement="bottom-right"
                  @confirm="handleDeleteTunnel"
                >
                  <t-button theme="danger" variant="text" :loading="isDeleting"> 删除隧道 </t-button>
                </t-popconfirm>
              </template>

              <div v-if="currentTunnel" class="detail-content">
                <div class="detail-header">
                  <h3>{{ currentTunnel.name }}</h3>
                  <p class="remarks">{{ currentTunnel.remarks || '暂无备注' }}</p>
                </div>

                <t-divider />

                <t-row :gutter="[24, 24]">
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">所在节点</span>
                      <span class="val">{{ currentNodeName }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">协议类型</span>
                      <span class="val uppercase">{{ currentTunnel.type }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">本地地址</span>
                      <span class="val">{{ currentTunnel.local_ip }}:{{ currentTunnel.local_port }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">远程端口</span>
                      <span class="val highlight">{{ currentTunnel.remote_port }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">今日流量</span>
                      <span class="val">{{ formatBytes(currentTunnel.today_traffic * 1024 * 1024) }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">总流量</span>
                      <span class="val">{{ formatBytes(currentTunnel.total_traffic * 1024 * 1024) }}</span>
                    </div>
                  </t-col>
                </t-row>

                <t-divider />

                <div class="action-area">
                  <t-button theme="primary" size="large" :loading="isAddingTunnel" block @click="handleUseTunnel">
                    <template #icon><play-circle-icon /></template>
                    使用此隧道
                  </t-button>
                </div>
              </div>

              <div v-else class="empty-detail">
                <cloud-icon size="48px" style="color: var(--td-text-color-disabled)" />
                <p>请在左侧选择一个隧道查看详情</p>
              </div>
            </t-card>
          </t-col>
        </t-row>
      </t-space>
    </div>
    <create-tunnel-dialog
      v-if="showCreateDialog"
      v-model:visible="showCreateDialog"
      :token="mslUserToken"
      @success="handleCreateSuccess"
    />
  </div>
</template>

<style scoped lang="less">
.page-container {
  margin: 0 auto;
}

// --- 登录页样式 ---
.login-container {
  width: 100%;
  height: 80vh;
  display: flex;
  justify-content: center;
  align-items: center;
}
.login-card {
  width: 100%;
  max-width: 400px;
  padding: 48px 32px;
  background: var(--td-bg-color-container);
  border-radius: var(--td-radius-large);
  box-shadow: var(--td-shadow-2);
  text-align: center;
}
.title {
  font-size: 24px;
  font-weight: 600;
  margin: 16px 0 8px;
}
.subtitle {
  color: var(--td-text-color-secondary);
  margin-bottom: 32px;
  font-size: 14px;
}

// --- 仪表盘样式 ---

// 用户信息卡片
.info-card {
  .stat-item {
    text-align: left;
    .label {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      margin-bottom: 4px;
    }
    .value {
      font-size: 16px;
      font-weight: 600;
      color: var(--td-text-color-primary);
    }
    .date-text {
      font-size: 14px;
    }
  }
}

// 卡片通用
.full-height-card {
  height: 100%; // 让左右两边高度一致
  min-height: 500px;
}

.list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

// 隧道列表
.tunnel-list {
  max-height: 400px;
  overflow-y: auto;
  margin: 0 -16px;
  padding: 0 8px;

  &::-webkit-scrollbar {
    width: 4px;
  }
  &::-webkit-scrollbar-thumb {
    background: var(--td-scrollbar-color);
    border-radius: 2px;
  }
}

.tunnel-item {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  margin-bottom: 8px;
  border-radius: var(--td-radius-medium);
  cursor: pointer;
  transition: all 0.2s;
  border: 1px solid transparent;

  &:hover {
    background-color: var(--td-bg-color-secondarycontainer);
  }

  &.active {
    background-color: var(--td-brand-color-light); // 选中态背景浅色
    border-color: var(--td-brand-color); // 选中边框
    .item-title {
      color: var(--td-brand-color);
    }
  }

  .item-icon {
    margin-right: 12px;
    font-size: 20px;
    color: var(--td-text-color-secondary);
  }

  .item-content {
    flex: 1;
    overflow: hidden;
    .item-title {
      font-weight: 500;
      font-size: 14px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .item-subtitle {
      font-size: 12px;
      color: var(--td-text-color-secondary);
    }
  }
}

// 详情区域
.detail-content {
  padding: 8px 0;

  .detail-header {
    h3 {
      margin: 0 0 8px;
      font-size: 20px;
    }
    .remarks {
      color: var(--td-text-color-secondary);
      font-size: 14px;
      margin: 0;
    }
  }

  .detail-grid {
    display: flex;
    flex-direction: column;
    .label {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      margin-bottom: 4px;
    }
    .val {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 500;
      word-break: break-all;
    }
    .highlight {
      color: var(--td-brand-color);
      font-weight: 700;
      font-size: 16px;
    }
    .uppercase {
      text-transform: uppercase;
    }
  }

  .action-area {
    margin-top: 32px;
  }
}

.empty-detail {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 300px;
  color: var(--td-text-color-disabled);
  p {
    margin-top: 16px;
  }
}
</style>
