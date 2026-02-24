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
      await createFrpTunnel(currentTunnel.value.proxyName, res.config, 'ME Frp');
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
  <div class="page-container">
    <div v-if="meUserToken === ''" class="login-container">
      <div class="login-card">
        <div class="icon-wrapper">
          <user-circle-icon size="56px" style="color: var(--td-brand-color)" />
        </div>
        <h2 class="title">登录 ME Frp</h2>
        <p class="subtitle">选择您的登录方式以接入服务</p>

        <t-tabs v-model="loginType" class="custom-tabs">
          <t-tab-panel value="password" label="账号密码登录" />
          <t-tab-panel value="token" label="Token 登录" />
        </t-tabs>

        <t-form
          v-if="loginType === 'password'"
          :data="loginForm"
          class="login-form"
          label-width="0"
          @submit="handlePasswordLogin"
        >
          <t-form-item name="username">
            <t-input v-model="loginForm.username" size="large" placeholder="请输入 ME Frp 账号" clearable>
              <template #prefix-icon><user-circle-icon /></template>
            </t-input>
          </t-form-item>
          <t-form-item name="password">
            <t-input v-model="loginForm.password" size="large" type="password" placeholder="请输入密码" clearable>
              <template #prefix-icon><lock-on-icon /></template>
            </t-input>
          </t-form-item>
          <t-form-item name="captchaCallback">
            <t-input v-model="loginForm.captchaCallback" size="large" placeholder="请粘贴获取到的验证码" clearable>
              <template #prefix-icon><secured-icon /></template>
              <template #suffix>
                <t-button variant="text" size="small" theme="primary" @click="openCaptchaPage"> 获取验证码 </t-button>
              </template>
            </t-input>
          </t-form-item>
          <div class="submit-btn-wrap">
            <t-button block theme="primary" type="submit" size="large" :loading="isLoggingIn">立即登录</t-button>
          </div>
        </t-form>

        <t-form v-else :data="loginForm" class="login-form" label-width="0" @submit="() => handleTokenLogin()">
          <t-form-item name="token">
            <t-input
              v-model="loginForm.token"
              size="large"
              type="password"
              placeholder="请输入 ME Frp 账户 Token"
              clearable
            >
              <template #prefix-icon><key-icon /></template>
            </t-input>
          </t-form-item>
          <div class="submit-btn-wrap">
            <t-button block theme="primary" type="submit" size="large" :loading="isLoggingIn">验证 Token</t-button>
          </div>
        </t-form>

        <div class="register-link">
          <t-button variant="text" size="small" @click="changeUrl('https://www.mefrp.com/register')"
            >还没有账户？注册 ME Frp</t-button
          >
        </div>
      </div>
    </div>

    <div v-else class="dashboard-container">
      <t-loading attach="#app-space" :loading="loading" text="加载数据中..." />

      <t-space id="app-space" direction="vertical" size="large" style="width: 100%">
        <t-card v-if="userInfo" :bordered="false" title="ME Frp 用户信息" class="info-card">
          <template #actions>
            <t-space size="small">
              <t-button
                variant="outline"
                theme="primary"
                size="small"
                :disabled="userInfo.todaySigned"
                @click="handleSign"
              >
                {{ userInfo.todaySigned ? '今日已签到' : '每日签到' }}
              </t-button>
              <t-tag theme="primary" variant="light-outline">{{ userInfo.friendlyGroup }}</t-tag>
              <t-popconfirm content="确认退出登录吗？" @confirm="handleLogout">
                <t-button variant="outline" theme="danger" size="small"> 退出登录 </t-button>
              </t-popconfirm>
            </t-space>
          </template>
          <t-row :gutter="[16, 16]">
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">用户昵称</div>
                <div class="value">
                  {{ userInfo.username }}
                  <t-tag v-if="userInfo.friendlyGroup !== '未实名'" theme="success" variant="dark" size="small"
                    >已实名</t-tag
                  >
                </div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">隧道使用情况</div>
                <div class="value">{{ userInfo.usedProxies }} / {{ userInfo.maxProxies }} 条</div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">速率限制</div>
                <div class="value">{{ userInfo.outBound ? Math.floor(userInfo.outBound / 128) : 0 }} Mbps</div>
              </div>
            </t-col>
            <t-col :xs="6" :sm="3">
              <div class="stat-item">
                <div class="label">剩余流量</div>
                <div class="value">{{ formatTraffic(userInfo.traffic) }}</div>
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
                    <t-button size="small" variant="text" :loading="loading" @click="handleRefresh">
                      <template #icon><refresh-icon /></template>刷新
                    </t-button>
                    <t-button size="small" variant="text" @click="handleAddTunnel">
                      <template #icon><add-icon /></template>新建
                    </t-button>
                  </t-space>
                </div>
              </template>

              <div class="tunnel-list">
                <div
                  v-for="tunnel in tunnels"
                  :key="tunnel.proxyId"
                  class="tunnel-item"
                  :class="{ active: selectedTunnelId === tunnel.proxyId }"
                  @click="selectedTunnelId = tunnel.proxyId"
                >
                  <div class="item-icon">
                    <server-icon />
                  </div>
                  <div class="item-content">
                    <div class="item-title">{{ tunnel.proxyName }}</div>
                    <div class="item-subtitle">{{ nodesMap[tunnel.nodeId] || `Node ${tunnel.nodeId}` }}</div>
                  </div>
                  <div class="item-status">
                    <t-tag v-if="tunnel.isOnline" theme="success" variant="light" size="small">在线</t-tag>
                    <t-tag v-else theme="default" variant="light" size="small">离线</t-tag>
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

              <template #actions>
                <t-popconfirm
                  v-if="currentTunnel"
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
                  <h3>{{ currentTunnel.proxyName }}</h3>
                  <p class="remarks">隧道 ID: {{ currentTunnel.proxyId }}</p>
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
                      <span class="label">本地地址</span>
                      <span class="val">{{ currentTunnel.localIp }}:{{ currentTunnel.localPort }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">远程端口</span>
                      <span class="val highlight">{{ currentTunnel.remotePort }}</span>
                    </div>
                  </t-col>
                  <t-col :span="6">
                    <div class="detail-grid">
                      <span class="label">当前状态</span>
                      <span class="val" :style="{ color: currentTunnel.isOnline ? 'var(--td-success-color)' : '' }">
                        {{ currentTunnel.isOnline ? '节点在线' : '离线' }}
                      </span>
                    </div>
                  </t-col>
                </t-row>

                <t-divider />

                <div class="action-area">
                  <t-button theme="primary" size="large" :loading="isAddingTunnel" block @click="handleUseTunnel">
                    <template #icon><play-circle-icon /></template>
                    启动此隧道映射
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
      :token="meUserToken"
      @success="handleCreateSuccess"
    />
  </div>
</template>

<style scoped lang="less">
.page-container {
  margin: 0 auto;
}

/* --- 登录页样式 --- */
.login-container {
  width: 100%;
  height: 80vh;
  display: flex;
  justify-content: center;
  align-items: center;
}
.login-card {
  width: 100%;
  max-width: 420px;
  padding: 48px 40px;
  background: var(--td-bg-color-container);
  border-radius: var(--td-radius-extra-large);
  box-shadow: var(--td-shadow-3);
  text-align: center;
}
.icon-wrapper {
  margin-bottom: 16px;
}
.title {
  font-size: 26px;
  font-weight: 600;
  margin: 0 0 8px;
  color: var(--td-text-color-primary);
}
.subtitle {
  color: var(--td-text-color-secondary);
  margin-bottom: 32px;
  font-size: 14px;
}

/* 强制让 Tabs 居中 */
.custom-tabs {
  margin-bottom: 24px;
  :deep(.t-tabs__nav-container) {
    display: flex;
    justify-content: center;
  }
  :deep(.t-tabs__nav) {
    margin: 0 auto;
  }
}

.login-form {
  text-align: left;
  :deep(.t-form__item) {
    margin-bottom: 24px;
  }
}
.submit-btn-wrap {
  margin-top: 32px;
}
.register-link {
  margin-top: 24px;
}

/* --- 仪表盘样式 --- */
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
  }
}
.full-height-card {
  height: 100%;
  min-height: 500px;
}
.list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
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
    background-color: var(--td-brand-color-light);
    border-color: var(--td-brand-color);
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
.empty-state {
  text-align: center;
  color: var(--td-text-color-disabled);
  padding: 32px 0;
}
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
:deep(.t-card__header) {
  overflow-x: auto;
  white-space: nowrap;
  flex-wrap: nowrap;
}
:deep(.t-card__header)::-webkit-scrollbar {
  display: none;
}
</style>
