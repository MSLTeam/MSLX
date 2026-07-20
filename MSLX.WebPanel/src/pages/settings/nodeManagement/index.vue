<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue';
import { linkSlaveNode, unlinkSlaveNode, postEditSlaveNode } from '@/api/node';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import { AddIcon, RefreshIcon, EditIcon, InfoCircleIcon, DeleteIcon, ServerIcon, HelpCircleIcon } from 'tdesign-icons-vue-next';
import { useUserStore, useNodeStore } from '@/store';
import * as signalR from '@microsoft/signalr';
import { request } from '@/utils/request';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';

const userStore = useUserStore();
const nodeStore = useNodeStore();

const nodes = computed(() => nodeStore.slaveNodes);
const loading = computed(() => nodeStore.loading);

const linkVisible = ref(false);
const isEdit = ref(false);
const editingId = ref('');
const linkForm = ref({
  nodeName: '',
  nodeUrl: '',
  nodeLogo: 'https://www.mslmc.cn/logo.png',
  masterUrl: '',
  linkKey: '',
  nodeTags: ''
});

const tagsArray = ref<string[]>([]);

const connection = ref<signalR.HubConnection | null>(null);
const slavesStats = ref<Record<string, any>>({});
const isSignalRConnected = ref(false);
const hasReceivedFirstPayload = ref(false);

const fetchNodes = async (force = false) => {
  await nodeStore.fetchNodes(force);
};

const handleAddNode = () => {
  isEdit.value = false;
  linkForm.value = { nodeName: '', nodeUrl: '', nodeLogo: 'https://www.mslmc.cn/logo.png', masterUrl: '', linkKey: '', nodeTags: '' };
  tagsArray.value = [];
  linkVisible.value = true;
};

const handleEditNode = (node: any) => {
  isEdit.value = true;
  editingId.value = node.nodeId;
  const rawTags = node.nodeTags || node.NodeTags || '';
  linkForm.value = {
    nodeName: node.nodeName,
    nodeUrl: node.nodeUrl,
    nodeLogo: node.nodeLogo || 'https://www.mslmc.cn/logo.png',
    masterUrl: node.masterUrl || '',
    linkKey: node.linkKey,
    nodeTags: rawTags
  };
  tagsArray.value = rawTags ? rawTags.split(/[，,]/).map((t: string) => t.trim()).filter(Boolean) : [];
  linkVisible.value = true;
};

const testConnection = async (url: string) => {
  try {
    const res = await fetch(`${url.replace(/\/$/, '')}/api/status`);
    if (res.ok || res.status === 401 || res.status === 404) return true;
    return false;
  } catch {
    return false;
  }
};

const handleLink = async () => {
  if (!linkForm.value.nodeName || !linkForm.value.nodeUrl || !linkForm.value.linkKey) {
    MessagePlugin.warning('请填写完整信息');
    return;
  }

  linkForm.value.nodeTags = tagsArray.value.join(',');

  try {
    if (isEdit.value) {
      await postEditSlaveNode(editingId.value, linkForm.value);
      MessagePlugin.success('节点修改成功');

      // 测试连通性
      const ok = await testConnection(linkForm.value.nodeUrl);
      if (!ok) {
        const alertObj = DialogPlugin.alert({
          header: '连通性警告',
          body: '配置已保存，但无法连接到该节点，请检查节点状态或配置是否正确。',
          theme: 'warning',
          onConfirm: () => {
            alertObj.hide();
          }
        });
      }
    } else {
      await linkSlaveNode(linkForm.value);
      MessagePlugin.success('节点链接成功');
    }

    linkVisible.value = false;
    fetchNodes(true);
  } catch (e: any) {
    MessagePlugin.error(`${isEdit.value ? '修改' : '链接'}失败: ${e.message}`);
  }
};

const handleUnlink = (row: any) => {
  const dialog = DialogPlugin.confirm({
    header: '解除链接',
    body: `确定要解除与节点 ${row.nodeName} 的链接吗？`,
    theme: 'danger',
    onConfirm: async () => {
      try {
        await unlinkSlaveNode(row.nodeId);
        MessagePlugin.success('解除链接成功');
        fetchNodes(true);
      } catch (e: any) {
        MessagePlugin.error(`解除链接失败: ${e.message}`);
      }
      dialog.hide();
    }
  });
};

const detailsVisible = ref(false);
const activeNodeDetails = ref<any>({});
const activeNodeStatus = ref<any>(null);
const fetchingStatus = ref(false);

const handleShowDetails = async (node: any) => {
  activeNodeDetails.value = node;
  activeNodeStatus.value = null;
  detailsVisible.value = true;
  fetchingStatus.value = true;

  try {
    const resData = await request.get({
      url: '/api/status',
      baseURL: node.nodeUrl,
      headers: {
        'x-node-id': node.nodeId
      }
    }, {
      requestToSlaveNode: true
    });
    activeNodeStatus.value = resData;
  } catch (e) {
    console.error('Failed to fetch node status', e);
  } finally {
    fetchingStatus.value = false;
  }
};

// 初始化 SignalR
const initSignalR = async () => {
  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/system', baseUrl || window.location.origin);
  if (token) hubUrl.searchParams.append('x-user-token', token);

  connection.value = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .withAutomaticReconnect()
    .build();

  connection.value.on('ReceiveSystemStats', (broadcast: any) => {
    hasReceivedFirstPayload.value = true;
    if (broadcast && broadcast.slaves) {
      slavesStats.value = broadcast.slaves;
    }
  });

  try {
    await connection.value.start();
    isSignalRConnected.value = true;
    await connection.value.invoke('JoinMonitor');
  } catch (error) {
    console.error('SignalR 连接失败:', error);
  }

  connection.value.onclose(() => {
    isSignalRConnected.value = false;
    hasReceivedFirstPayload.value = false;
  });
};

onMounted(() => {
  fetchNodes();
  initSignalR();
});

onUnmounted(async () => {
  if (connection.value) {
    try {
      await connection.value.invoke('LeaveMonitor');
      await connection.value.stop();
    } catch (e) {
      console.error(e);
    }
  }
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">节点管理</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">管理您的子节点映射，查看所有节点运行状态</p>
      </div>

      <div class="flex items-center gap-2 sm:gap-3 flex-wrap">
        <t-button variant="outline" theme="default" class="!rounded-lg" @click="changeUrl(DOC_URLS.multi_nodes_doc)">
          <template #icon><help-circle-icon /></template>
          文档
        </t-button>
        <t-button variant="dashed" @click="fetchNodes(true)">
          <template #icon><refresh-icon /></template>
          刷新
        </t-button>
        <t-button theme="primary" @click="handleAddNode">
          <template #icon><add-icon /></template>
          添加节点
        </t-button>
      </div>
    </div>

    <t-alert theme="warning" variant="light" title="子节点功能为测试功能">
      <template #message>
        分布式子节点管理涉及较为复杂的远程通信与网络鉴权，目前<strong>仍处于开发及测试阶段</strong>。此功能仅供测试体验，请<strong>切勿将其直接部署于商业化或关键性生产业务环境</strong>，以规避可能出现的不稳定风险。如有疑问或遇到 Bug，欢迎前往 <a href="javascript:void(0)" class="text-blue-500 font-bold hover:underline" @click="changeUrl(DOC_URLS.github_issues)">GitHub Issues</a> 提交反馈。
      </template>
    </t-alert>

    <div class="relative min-h-[400px]">
      <div v-if="loading" class="flex flex-col items-center justify-center py-24">
        <t-loading size="medium" text="正在获取节点列表..." />
      </div>

      <div v-else-if="nodes.length === 0" class="flex flex-col items-center justify-center py-24 text-[var(--td-text-color-secondary)]">
        暂无任何子节点，请点击右上角添加。
      </div>

      <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
        <div
          v-for="item in nodes"
          :key="item.nodeId"
          class="design-card flex flex-col p-5 bg-white dark:bg-zinc-800 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md transition-all duration-300"
        >
          <div class="flex items-start gap-4 mb-4">
            <div class="w-12 h-12 flex-shrink-0 flex items-center justify-center rounded-xl bg-blue-50 dark:bg-blue-500/10 text-blue-500 text-2xl overflow-hidden">
              <img v-if="item.nodeLogo" :src="item.nodeLogo" class="w-full h-full object-cover"  alt="节点logo"/>
              <server-icon v-else />
            </div>
            <div class="flex-1 min-w-0 flex flex-col justify-center">
              <h3 class="text-base font-semibold text-[var(--td-text-color-primary)] truncate m-0 flex items-center gap-2" :title="item.nodeName">
                <span
                  class="w-2 h-2 rounded-full flex-shrink-0"
                  :class="slavesStats[item.nodeId] ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]' : 'bg-amber-400 shadow-[0_0_8px_rgba(251,191,36,0.5)]'"
                  :title="slavesStats[item.nodeId] ? '节点在线' : '节点离线或未就绪'"
                ></span>
                {{ item.nodeName }}
              </h3>
              <p class="text-xs text-[var(--td-text-color-secondary)] m-0 mt-2 flex items-center gap-1.5 w-full" :title="item.nodeUrl">
                <span class="truncate flex-1 min-w-0">{{ item.nodeUrl ? item.nodeUrl.replace(/^https?:\/\//i, '') : '' }}</span>
                <span
                  v-if="item.nodeUrl && /^https:\/\//i.test(item.nodeUrl)"
                  class="inline-flex items-center gap-1.5 text-[11px] font-bold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-500/10 px-1.5 py-0.5 rounded-md border border-emerald-200/50 dark:border-emerald-500/20 shrink-0"
                >
                  <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
                  SSL
                </span>
                <span
                  v-else-if="item.nodeUrl"
                  class="inline-flex items-center gap-1.5 text-[11px] font-bold text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-500/10 px-1.5 py-0.5 rounded-md border border-amber-200/50 dark:border-amber-500/20 shrink-0"
                >
                  <span class="w-1.5 h-1.5 rounded-full bg-amber-500 animate-pulse"></span>
                  未加密
                </span>
              </p>
              <div v-if="item.nodeTags || item.NodeTags" class="flex flex-wrap gap-1.5 mt-2">
                <t-tag
                  v-for="(tag, idx) in (item.nodeTags || item.NodeTags).split(/[，,]/).map((t: string) => t.trim()).filter(Boolean)"
                  :key="tag"
                  size="small"
                  variant="light"
                  :theme="['primary', 'success', 'warning', 'danger'][idx % 4] as any"
                >
                  {{ tag }}
                </t-tag>
              </div>
            </div>
          </div>

          <div class="flex-1 flex flex-col gap-3 py-3 border-y border-[var(--td-component-border)]">
            <template v-if="slavesStats[item.nodeId]">
              <div>
                <div class="flex justify-between text-xs mb-1">
                  <span class="text-[var(--td-text-color-secondary)]">CPU 使用率</span>
                  <span class="font-mono">{{ slavesStats[item.nodeId].cpu }}%</span>
                </div>
                <t-progress :percentage="slavesStats[item.nodeId].cpu" :label="false" :color="slavesStats[item.nodeId].cpu > 80 ? 'var(--td-error-color)' : 'var(--td-brand-color)'" />
              </div>
              <div>
                <div class="flex justify-between text-xs mb-1">
                  <span class="text-[var(--td-text-color-secondary)]">内存使用率</span>
                  <span class="font-mono">{{ slavesStats[item.nodeId].memUsage }}%</span>
                </div>
                <t-progress :percentage="slavesStats[item.nodeId].memUsage" :label="false" :color="slavesStats[item.nodeId].memUsage > 80 ? 'var(--td-error-color)' : 'var(--td-success-color)'" />
              </div>
            </template>
            <div v-else-if="!isSignalRConnected || !hasReceivedFirstPayload" class="flex items-center justify-center h-full text-sm text-[var(--td-text-color-secondary)] gap-2">
              <t-loading size="small" />
              正在等待性能数据...
            </div>
            <div v-else class="flex items-center justify-center h-full text-sm text-[var(--td-text-color-secondary)]">
              暂无性能数据 (节点离线或未就绪)
            </div>
          </div>

          <div class="flex items-center justify-between mt-4 gap-2">
            <t-button variant="text" size="small" @click="handleShowDetails(item)">
              <template #icon><info-circle-icon /></template>
              详细
            </t-button>
            <div class="flex gap-2">
              <t-button variant="outline" size="small" @click="handleEditNode(item)">
                <template #icon><edit-icon /></template>
                编辑
              </t-button>
              <t-button theme="danger" variant="outline" size="small" @click="handleUnlink(item)">
                <template #icon><delete-icon /></template>
                解除
              </t-button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 链接/编辑 节点弹窗 -->
    <t-dialog v-model:visible="linkVisible" :header="isEdit ? '编辑节点' : '链接新节点'" @confirm="handleLink">
      <t-form :data="linkForm">
        <t-form-item label="节点名称" name="nodeName">
          <t-input v-model="linkForm.nodeName" placeholder="请输入节点名称" />
        </t-form-item>
        <t-form-item label="节点地址" name="nodeUrl">
          <t-input v-model="linkForm.nodeUrl" placeholder="如 http://192.168.1.100:1027" />
        </t-form-item>
        <t-form-item label="节点Logo" name="nodeLogo">
          <t-input v-model="linkForm.nodeLogo" placeholder="如 https://www.mslmc.cn/logo.png" />
        </t-form-item>
        <t-form-item label="通讯密钥" name="linkKey">
          <t-input v-model="linkForm.linkKey" type="password" placeholder="请输入子节点的 LinkKey" />
        </t-form-item>
         <t-form-item label="主节点地址" name="masterUrl" help="覆盖自动识别的主节点地址（留空则自动检测）">
          <t-input v-model="linkForm.masterUrl" placeholder="如 http://192.168.1.50:1027" />
        </t-form-item>
        <t-form-item label="节点标签" name="nodeTags" help="输入标签后按回车(Enter)添加标签">
          <t-tag-input v-model="tagsArray" placeholder="请输入节点标签并回车" clearable />
        </t-form-item>
      </t-form>
    </t-dialog>

    <!-- 节点详情弹窗 -->
    <t-dialog v-model:visible="detailsVisible" header="节点详情" :footer="false">
      <div class="flex flex-col gap-3 text-sm">
        <div class="flex justify-between border-b border-[var(--td-component-border)] pb-2">
          <span class="text-[var(--td-text-color-secondary)]">节点标识</span>
          <span class="font-mono text-[var(--td-text-color-primary)]">{{ activeNodeDetails.nodeId }}</span>
        </div>
        <div class="flex justify-between border-b border-[var(--td-component-border)] pb-2">
          <span class="text-[var(--td-text-color-secondary)]">节点名称</span>
          <span class="text-[var(--td-text-color-primary)]">{{ activeNodeDetails.nodeName }}</span>
        </div>
        <div v-if="activeNodeDetails.nodeTags || activeNodeDetails.NodeTags" class="flex justify-between border-b border-[var(--td-component-border)] pb-2 items-center">
          <span class="text-[var(--td-text-color-secondary)]">节点标签</span>
          <div class="flex flex-wrap gap-1">
            <t-tag
              v-for="(tag, idx) in (activeNodeDetails.nodeTags || activeNodeDetails.NodeTags).split(/[，,]/).map((t: string) => t.trim()).filter(Boolean)"
              :key="tag"
              size="small"
              variant="light"
              :theme="['primary', 'success', 'warning', 'danger'][idx % 4] as any"
            >
              {{ tag }}
            </t-tag>
          </div>
        </div>
        <div class="flex justify-between border-b border-[var(--td-component-border)] pb-2 items-center">
          <span class="text-[var(--td-text-color-secondary)]">节点地址</span>
          <span class="text-[var(--td-text-color-primary)] flex items-center gap-1.5 max-w-[70%]">
            <span class="truncate flex-1 min-w-0">{{ activeNodeDetails.nodeUrl ? activeNodeDetails.nodeUrl.replace(/^https?:\/\//i, '') : '' }}</span>
            <span
              v-if="activeNodeDetails.nodeUrl && /^https:\/\//i.test(activeNodeDetails.nodeUrl)"
              class="inline-flex items-center gap-1.5 text-[11px] font-bold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-500/10 px-1.5 py-0.5 rounded-md border border-emerald-200/50 dark:border-emerald-500/20 shrink-0"
            >
              <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span>
              SSL 安全
            </span>
            <span
              v-else-if="activeNodeDetails.nodeUrl"
              class="inline-flex items-center gap-1.5 text-[11px] font-bold text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-500/10 px-1.5 py-0.5 rounded-md border border-amber-200/50 dark:border-amber-500/20 shrink-0"
            >
              <span class="w-1.5 h-1.5 rounded-full bg-amber-500 animate-pulse"></span>
              未加密
            </span>
          </span>
        </div>
        <div class="flex justify-between border-b border-[var(--td-component-border)] pb-2">
          <span class="text-[var(--td-text-color-secondary)]">通讯密钥</span>
          <span class="font-mono text-[var(--td-text-color-primary)]">{{ activeNodeDetails.commsKey }}</span>
        </div>
        <div class="flex justify-between border-b border-[var(--td-component-border)] pb-2">
          <span class="text-[var(--td-text-color-secondary)]">链接时间</span>
          <span class="text-[var(--td-text-color-primary)]">{{ new Date(activeNodeDetails.linkedAt).toLocaleString() }}</span>
        </div>

        <div class="mt-2">
          <h4 class="text-sm font-bold text-[var(--td-text-color-primary)] mb-2">节点系统信息</h4>
          <div v-if="fetchingStatus" class="flex items-center justify-center py-4">
            <t-loading size="small" text="正在获取节点状态..." />
          </div>
          <div v-else-if="!activeNodeStatus" class="flex items-center justify-center py-4 text-[var(--td-text-color-secondary)]">
            无法连接到节点，或节点离线
          </div>
          <div v-else class="flex flex-col gap-3 p-3 bg-[var(--td-bg-color-container)] rounded-xl border border-[var(--td-component-border)]">
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">主机名</span>
              <span class="text-[var(--td-text-color-primary)]">{{ activeNodeStatus.systemInfo?.hostname }}</span>
            </div>
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">操作系统</span>
              <span class="text-[var(--td-text-color-primary)]">{{ activeNodeStatus.systemInfo?.osType }} ({{ activeNodeStatus.systemInfo?.osArchitecture }})</span>
            </div>
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">系统版本</span>
              <span class="text-[var(--td-text-color-primary)] truncate max-w-[200px]" :title="activeNodeStatus.systemInfo?.osVersion">{{ activeNodeStatus.systemInfo?.osVersion }}</span>
            </div>
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">运行时</span>
              <span class="text-[var(--td-text-color-primary)]">{{ activeNodeStatus.systemInfo?.netVersion }}</span>
            </div>
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">MSLX Daemon 版本</span>
              <span class="font-mono text-[var(--td-text-color-primary)]">{{ activeNodeStatus.version }}</span>
            </div>
            <div class="flex justify-between">
              <span class="text-[var(--td-text-color-secondary)]">运行于 Docker</span>
              <t-tag :theme="activeNodeStatus.systemInfo?.docker ? 'success' : 'default'" size="small" variant="light-outline">
                {{ activeNodeStatus.systemInfo?.docker ? '是哦' : '不是哦' }}
              </t-tag>
            </div>
          </div>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped>
.design-card {
  transition: all 0.3s ease;
}
.design-card:hover {
  transform: translateY(-2px);
}

/* 修复 t-tag-input 标签垂直拉伸换行问题 */
:deep(.t-tag-input) {
  height: auto !important;
  min-height: 36px !important;
  padding: 4px 8px !important;
  display: inline-flex !important;
  flex-wrap: wrap !important;
  align-items: center !important;
}

:deep(.t-tag-input .t-input__prefix) {
  display: flex !important;
  flex-wrap: wrap !important;
  align-items: center !important;
  gap: 4px !important;
}

:deep(.t-tag-input .t-tag) {
  display: inline-flex !important;
  width: auto !important;
  max-width: 100% !important;
  margin: 2px 0 !important;
}
</style>
