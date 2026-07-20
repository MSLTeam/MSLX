<script setup lang="ts">
import { onMounted, reactive, ref, computed } from 'vue';
import { MessagePlugin, DialogPlugin, type FormRules, type PrimaryTableCol, type TableRowData } from 'tdesign-vue-next';
import { AddIcon, SearchIcon, RefreshIcon } from 'tdesign-icons-vue-next';

import { getUserList, createUser, updateUser, deleteUser } from '@/api/user';
import type { AdminUserDto } from '@/api/model/user';
import { useUserStore, useNodeStore } from '@/store';
import { request } from '@/utils/request';

// --- 状态定义 ---
const loading = ref(false);
const tableData = ref<AdminUserDto[]>([]);
const filterText = ref('');

const userStore = useUserStore();
const nodeStore = useNodeStore();

const dialogVisible = ref(false);
const dialogMode = ref<'create' | 'edit'>('create');
const submitLoading = ref(false);
const resourceOptions = ref<any[]>([]);

// 二级资源分配弹窗
const resourceDialogVisible = ref(false);
const activeNodeTab = ref('local');

const invalidResources = computed(() => {
  if (resourceOptions.value.length === 0) return [];
  const validSet = new Set<string>();
  for (const node of resourceOptions.value) {
    if (node.children) {
      for (const group of node.children) {
        if (group.children) {
          for (const item of group.children) {
            validSet.add(item.value);
          }
        }
      }
    }
  }
  return formData.resources.filter(r => !validSet.has(r));
});

const cleanInvalidResources = () => {
  if (resourceOptions.value.length === 0) return;
  const validSet = new Set<string>();
  for (const node of resourceOptions.value) {
    if (node.children) {
      for (const group of node.children) {
        if (group.children) {
          for (const item of group.children) {
            validSet.add(item.value);
          }
        }
      }
    }
  }
  formData.resources = formData.resources.filter(r => validSet.has(r));
  MessagePlugin.success('已清理无效关联资源');
};

// -----------------------

const formData = reactive({
  id: '',
  username: '',
  name: '',
  password: '',
  role: 'admin',
  resetApiKey: false,
  resources: [] as string[],
});

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', type: 'error' }],
  role: [{ required: true, message: '请选择角色', type: 'error' }],
  password: [
    {
      validator: (val) => {
        if (dialogMode.value === 'create' && !val)
          return { result: false, message: '创建用户时密码必填', type: 'error' };
        return true;
      },
    },
  ],
};

// 获取全节点资源选项
const fetchNodeResources = async () => {
  const nodes = [{ nodeId: 'local', nodeName: '主节点', nodeUrl: '' }, ...nodeStore.slaveNodes];
  const options = [];

  for (const node of nodes) {
    try {
      const _nodeId = node.nodeId || node.NodeId;
      const _nodeUrl = node.nodeUrl || node.NodeUrl || '';
      const _nodeName = node.nodeName || node.NodeName || '未知节点';

      const isLocal = _nodeId === 'local';
      const baseUrl = isLocal ? '' : _nodeUrl.replace(/\/$/, '');
      const reqOpts = isLocal ? { requestToSlaveNode: false } : { requestToSlaveNode: true, withToken: true };

      const [instancesRes, frpRes] = await Promise.all([
        request.get({ url: '/api/instance/list', baseURL: baseUrl, headers: isLocal ? {} : { 'x-node-id': _nodeId } }, reqOpts).catch(() => []),
        request.get({ url: '/api/frp/list', baseURL: baseUrl, headers: isLocal ? {} : { 'x-node-id': _nodeId } }, reqOpts).catch(() => []),
      ]);

      const children = [];

      if (instancesRes && instancesRes.length) {
        children.push({
          label: '实例 (Server)',
          value: `${_nodeId}_servers`,
          children: instancesRes.map((item: any) => ({
            label: `[${item.id ?? item.ID}] ${item.name ?? item.Name}`,
            value: isLocal ? `server:${item.id ?? item.ID}` : `${_nodeId}_server:${item.id ?? item.ID}`,
          })),
        });
      }

      if (frpRes && frpRes.length) {
        children.push({
          label: '隧道 (FRP)',
          value: `${_nodeId}_frps`,
          children: frpRes.map((item: any) => ({
            label: `[${item.id ?? item.ID}] ${item.name ?? item.Name}`,
            value: isLocal ? `frp:${item.id ?? item.ID}` : `${_nodeId}_frp:${item.id ?? item.ID}`,
          })),
        });
      }

      if (children.length > 0) {
        options.push({
          label: _nodeName,
          value: _nodeId,
          children,
        });
      }
    } catch (e) {
      console.warn(`节点资源获取失败： ${node.nodeName || node.NodeName}`, e);
    }
  }

  resourceOptions.value = options;
};

// 表格列定义
const columns = computed((): PrimaryTableCol<TableRowData>[] => [
  { colKey: 'info', title: '用户信息', width: 200, fixed: 'left', cell: 'info-slot' },
  { colKey: 'role', title: '角色', width: 100, cell: 'role-slot' },
  { colKey: 'lastLogin', title: '最后登录', width: 180, cell: 'time-slot', className: 'hidden-xs' },
  { colKey: 'op', title: '操作', width: 140, fixed: 'right', cell: 'op-slot' },
]);

// 过滤数据
const displayData = computed(() => {
  if (!filterText.value) return tableData.value;
  const key = filterText.value.toLowerCase();
  return tableData.value.filter(
    (item) => item.username.toLowerCase().includes(key) || (item.name && item.name.toLowerCase().includes(key)),
  );
});

const fetchData = async () => {
  loading.value = true;
  try {
    const res = await getUserList();
    tableData.value = res;
  } catch (e: any) {
    MessagePlugin.error(e.message || '获取用户列表失败');
  } finally {
    loading.value = false;
  }
};

const handleAdd = () => {
  dialogMode.value = 'create';
  formData.id = '';
  formData.username = '';
  formData.name = '';
  formData.password = '';
  formData.role = 'user';
  formData.resetApiKey = false;
  formData.resources = [];

  if (resourceOptions.value.length === 0) {
    fetchNodeResources();
  }

  dialogVisible.value = true;
};

const handleEdit = (row: AdminUserDto) => {
  dialogMode.value = 'edit';
  formData.id = row.id;
  formData.username = row.username;
  formData.name = row.name;
  formData.password = '';
  formData.role = row.role;
  formData.resetApiKey = false;
  formData.resources = row.resources ? [...row.resources] : [];

  if (resourceOptions.value.length === 0) {
    fetchNodeResources();
  }

  dialogVisible.value = true;
};

const onSubmit = async ({ validateResult }: any) => {
  if (validateResult !== true) return;

  submitLoading.value = true;
  try {
    const finalResources = formData.role === 'admin' ? [] : [...formData.resources];

    if (dialogMode.value === 'create') {
      await createUser({
        username: formData.username,
        password: formData.password,
        name: formData.name,
        role: formData.role,
        resources: finalResources,
      });
      MessagePlugin.success('用户创建成功');
    } else {
      await updateUser(formData.id, {
        name: formData.name,
        password: formData.password || undefined,
        role: formData.role,
        resetApiKey: formData.resetApiKey,
        resources: finalResources,
      });
      MessagePlugin.success('用户更新成功');
    }
    dialogVisible.value = false;
    fetchData();
  } catch (e: any) {
    MessagePlugin.error(e.message || '操作失败');
  } finally {
    submitLoading.value = false;
  }
};

const handleDelete = (row: AdminUserDto) => {
  const confirmDia = DialogPlugin.confirm({
    header: '删除警告',
    body: `确定要删除用户 "${row.username}" 吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      try {
        confirmDia.hide();
        await deleteUser(row.id);
        MessagePlugin.success('删除成功');
        fetchData();
      } catch (e: any) {
        MessagePlugin.error(e.message || '删除失败');
      }
    },
  });
};

const paginationConfig = computed(() => ({
  defaultPageSize: 20,
  total: displayData.value.length,
  layout: window.innerWidth < 768 ? 'total, prev, pager, next' : 'total, size, prev, pager, next, jumper',
  maxPageBtnNum: window.innerWidth < 768 ? 3 : 5,
}));

onMounted(async () => {
  fetchData();
  await nodeStore.fetchNodes();
  fetchNodeResources();
});
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">用户管理</h2>
          <span class="text-xs text-[var(--td-text-color-secondary)] mt-1.5 font-medium"
            >管理系统内的账户权限与实例资源分配</span
          >
        </div>
      </div>

      <div class="flex items-center gap-3">
        <t-button variant="dashed" @click="fetchData">
          <template #icon><refresh-icon /></template>
          刷新数据
        </t-button>
        <t-button theme="primary" @click="handleAdd">
          <template #icon><add-icon /></template>
          新增用户
        </t-button>
      </div>
    </div>

    <div class="relative min-h-[400px]">
      <div
        class="design-card list-item-anim flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5 sm:p-6"
        style="animation-delay: 0.05s"
      >
        <div
          class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
        >
          <div class="text-base font-bold text-[var(--td-text-color-primary)]">用户列表</div>
          <div class="w-full sm:w-72">
            <t-input v-model="filterText" placeholder="搜索用户名或昵称" clearable>
              <template #prefix-icon><search-icon class="opacity-60" /></template>
            </t-input>
          </div>
        </div>

        <div class="hidden sm:block">
          <t-table
            row-key="id"
            :data="displayData"
            :columns="columns"
            :loading="loading"
            :hover="true"
            :pagination="paginationConfig"
            class="!bg-transparent"
            table-layout="auto"
          >
            <template #info-slot="{ row }">
              <div class="flex items-center gap-3 py-1">
                <t-avatar
                  :image="row.avatar"
                  size="44px"
                  shape="circle"
                  class="shrink-0 ring-2 ring-zinc-100 dark:ring-zinc-700/50 shadow-sm !bg-[var(--color-primary)]/10 !text-[var(--color-primary)]"
                  :hide-on-load-failed="false"
                >
                  <span class="font-bold text-lg">{{ row.name ? row.name[0].toUpperCase() : 'U' }}</span>
                </t-avatar>
                <div class="flex flex-col min-w-0">
                  <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                    {{ row.name || '未设置昵称' }}
                  </div>
                  <div class="text-xs font-mono text-[var(--td-text-color-secondary)] mt-0.5 truncate">
                    @{{ row.username }}
                  </div>
                </div>
              </div>
            </template>

            <template #role-slot="{ row }">
              <span
                v-if="row.role === 'admin'"
                class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-success)]/10 text-[var(--color-success)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-success)]/20 shadow-sm"
              >
                管理员
              </span>
              <span
                v-else
                class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-primary)]/10 text-[var(--color-primary)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-primary)]/20 shadow-sm"
              >
                普通用户
              </span>
            </template>

            <template #time-slot="{ row }">
              <div class="flex items-center gap-1.5">
                <time-icon v-if="row.lastLoginTime" class="text-[var(--color-primary)] opacity-70" size="14px" />
                <span
                  v-if="row.lastLoginTime"
                  class="text-xs font-mono font-medium text-[var(--td-text-color-secondary)]"
                  >{{ new Date(row.lastLoginTime).toLocaleString() }}</span
                >
                <span
                  v-else
                  class="text-xs font-medium px-2 py-0.5 rounded-full bg-zinc-100 dark:bg-zinc-800 text-[var(--td-text-color-secondary)]"
                  >从未登录</span
                >
              </div>
            </template>

            <template #op-slot="{ row }">
              <div class="flex items-center gap-1">
                <t-button
                  variant="text"
                  theme="primary"
                  size="small"
                  class="hover:!bg-[var(--color-primary)]/10"
                  @click="handleEdit(row)"
                >
                  编辑
                </t-button>
                <div class="w-[1px] h-3 bg-zinc-200 dark:bg-zinc-700 mx-1"></div>
                <t-button
                  variant="text"
                  theme="danger"
                  size="small"
                  class="hover:!bg-red-500/10"
                  :disabled="row.id === userStore.userInfo.id || row.username === 'admin'"
                  @click="handleDelete(row)"
                >
                  删除
                </t-button>
              </div>
            </template>
          </t-table>
        </div>

        <div v-loading="loading" class="sm:hidden flex flex-col gap-4">
          <div v-if="displayData.length === 0" class="text-center py-8 text-zinc-400 text-sm">暂无数据</div>
          <div
            v-for="row in displayData"
            :key="row.id"
            class="p-4 design-card rounded-xl border border-[var(--td-component-border)] bg-[var(--td-bg-color-container)] flex flex-col gap-3 text-left"
          >
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-3">
                <t-avatar
                  :image="row.avatar"
                  size="40px"
                  shape="circle"
                  class="!bg-[var(--color-primary)]/10 !text-[var(--color-primary)]"
                >
                  <span class="font-bold text-base">{{ row.name ? row.name[0].toUpperCase() : 'U' }}</span>
                </t-avatar>
                <div class="flex flex-col min-w-0">
                  <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                    {{ row.name || '未设置昵称' }}
                  </div>
                  <div class="text-xs font-mono text-[var(--td-text-color-secondary)] truncate">
                    @{{ row.username }}
                  </div>
                </div>
              </div>
              <div>
                <span
                  v-if="row.role === 'admin'"
                  class="inline-flex items-center px-2 py-0.5 rounded bg-[var(--color-success)]/10 text-[var(--color-success)] font-extrabold text-[10px] uppercase border border-[var(--color-success)]/20"
                  >管理员</span
                >
                <span
                  v-else
                  class="inline-flex items-center px-2 py-0.5 rounded bg-[var(--color-primary)]/10 text-[var(--color-primary)] font-extrabold text-[10px] uppercase border border-[var(--color-primary)]/20"
                  >普通用户</span
                >
              </div>
            </div>

            <div class="flex items-center justify-between pt-2 border-t border-zinc-100 dark:border-zinc-800/80">
              <div class="text-[11px] text-[var(--td-text-color-secondary)]">
                <span v-if="row.lastLoginTime">{{ new Date(row.lastLoginTime).toLocaleString() }}</span>
                <span v-else>从未登录</span>
              </div>
              <div class="flex items-center gap-1">
                <t-button variant="text" theme="primary" size="small" @click="handleEdit(row)">编辑</t-button>
                <t-button
                  variant="text"
                  theme="danger"
                  size="small"
                  :disabled="row.id === userStore.userInfo.id || row.username === 'admin'"
                  @click="handleDelete(row)"
                  >删除</t-button
                >
              </div>
            </div>
          </div>
        </div>

        <div class="sm:hidden mt-4 flex justify-center overflow-x-auto">
          <t-pagination v-bind="paginationConfig" size="small" />
        </div>
      </div>
    </div>

    <t-dialog
      v-model:visible="dialogVisible"
      :header="dialogMode === 'create' ? '新增用户' : '编辑用户'"
      :confirm-btn="{ content: '提交保存', loading: submitLoading, theme: 'primary' }"
      :on-confirm="() => ($refs.formRef as any).submit()"
      width="520px"
      placement="center"
    >
      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" @submit="onSubmit" class="mt-4">
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-4">
          <t-form-item label="登录账号" name="username">
            <t-input v-model="formData.username" placeholder="请输入英文/数字账号" :disabled="dialogMode === 'edit'" />
          </t-form-item>

          <t-form-item label="显示昵称" name="name">
            <t-input v-model="formData.name" placeholder="请输入前台展示名称" />
          </t-form-item>
        </div>

        <t-form-item label="账户角色" name="role">
          <t-radio-group v-model="formData.role" variant="default-filled">
            <t-radio-button value="user">普通用户</t-radio-button>
            <t-radio-button value="admin">全局管理员</t-radio-button>
          </t-radio-group>
        </t-form-item>

        <template v-if="formData.role === 'user'">
          <t-form-item v-if="formData.role === 'user'" label-width="0">
            <t-alert theme="warning" variant="light" title="资源权限限制说明">
              <template #message
                >资源分配仅实现基础权限隔离，实例文件系统并非物理隔离。用户可能通过程序路径穿越访问敏感数据，请仅在信任伙伴间使用，<strong
                  style="color: var(--td-error-color)"
                  >如需要进行分发/商业化用途，请务必使用Docker！</strong
                >。
              </template>
            </t-alert>
          </t-form-item>

          <t-form-item label="分配资源" name="resources">
            <div class="flex flex-col gap-2 w-full">
              <t-button variant="dashed" block @click="resourceDialogVisible = true">
                已分配 {{ formData.resources.length }} 个资源，点击配置
              </t-button>
              <t-alert v-if="invalidResources.length > 0" theme="error" class="mt-1 !text-xs !py-1.5 !px-3">
                <template #message>
                  检测到 {{ invalidResources.length }} 个失效或离线的废弃资源。
                </template>
                <template #operation>
                  <span class="text-blue-500 cursor-pointer font-bold hover:underline" @click="cleanInvalidResources">清理</span>
                </template>
              </t-alert>
            </div>
            <template #help>
              <span class="text-[11px] text-[var(--td-text-color-secondary)] mt-1 inline-block"
                >该用户将获得以上选定实例和隧道的完整控制权</span
              >
            </template>
          </t-form-item>
        </template>

        <t-form-item label="密码设置" name="password">
          <t-input v-model="formData.password" type="password" placeholder="设置新密码" autocomplete="new-password" />
          <template #help>
            <span
              v-if="dialogMode === 'edit'"
              class="text-[11px] text-[var(--td-text-color-secondary)] mt-1 inline-block"
              >留空则保持原密码不变</span
            >
          </template>
        </t-form-item>

        <t-form-item v-if="dialogMode === 'edit'" label="开发者选项">
          <div
            class="flex items-center gap-3 bg-zinc-50 dark:bg-zinc-800/50 p-3 rounded-xl border border-[var(--td-component-border)] w-full mt-1"
          >
            <t-checkbox v-model="formData.resetApiKey">强制重置该用户的 API Key</t-checkbox>
          </div>
        </t-form-item>
      </t-form>
    </t-dialog>
    <!-- 资源配置二级弹窗 -->
    <t-dialog
      v-model:visible="resourceDialogVisible"
      header="配置跨节点资源分配"
      width="800px"
      :footer="false"
      destroy-on-close
    >
      <div class="flex flex-col gap-4">
        <div class="text-sm text-[var(--td-text-color-secondary)] bg-[var(--td-bg-color-container)]/50 p-3 rounded-lg">
          请在下方各节点选项卡中，勾选需要分配给当前用户的实例或隧道。
        </div>
        <t-tabs v-model="activeNodeTab">
          <t-tab-panel v-for="node in resourceOptions" :key="node.value" :value="node.value" :label="node.label">
            <div class="p-4 flex flex-col gap-6 max-h-[50vh] overflow-y-auto custom-scroll">
              <template v-if="node.children && node.children.length > 0">
                <div v-for="group in node.children" :key="group.value" class="bg-[var(--td-bg-color-container)] border border-[var(--td-component-border)] p-4 rounded-xl shadow-sm">
                  <div class="text-sm font-bold text-[var(--td-text-color-primary)] mb-4 border-b border-dashed border-zinc-200 dark:border-zinc-700 pb-2">
                    {{ group.label }}
                  </div>
                  <t-checkbox-group v-model="formData.resources" class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    <t-checkbox v-for="item in group.children" :key="item.value" :value="item.value" class="!w-full truncate hover:bg-zinc-100 dark:hover:bg-zinc-800 p-2 rounded-lg transition-colors">
                      {{ item.label }}
                    </t-checkbox>
                  </t-checkbox-group>
                  <div v-if="!group.children || group.children.length === 0" class="text-xs text-[var(--td-text-color-secondary)] py-2">
                    暂无可用资源
                  </div>
                </div>
              </template>
              <div v-else class="text-center py-10 text-[var(--td-text-color-secondary)]">
                该节点暂无任何实例或隧道资源
              </div>
            </div>
          </t-tab-panel>
        </t-tabs>
        <div class="flex justify-end pt-4 mt-2 border-t border-dashed border-[var(--td-component-border)]">
          <t-button theme="primary" @click="resourceDialogVisible = false">确认选择</t-button>
        </div>
      </div>
    </t-dialog>

  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* 首次渲染阶梯滑入动画 */
.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
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

:deep(.hidden-xs) {
  @media (max-width: 768px) {
    display: none !important;
  }
}

/* 优化表格内的滚动条 */
:deep(.t-table__content) {
  scrollbar-width: thin;
  scrollbar-color: rgba(161, 161, 170, 0.3) transparent;
}
:deep(.t-table__content::-webkit-scrollbar) {
  width: 6px;
  height: 6px;
}
:deep(.t-table__content::-webkit-scrollbar-thumb) {
  background-color: rgba(161, 161, 170, 0.3);
  border-radius: 4px;
}
</style>
