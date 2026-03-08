<script setup lang="ts">
import { onMounted, reactive, ref, computed } from 'vue';
import { MessagePlugin, DialogPlugin, type FormRules, type PrimaryTableCol, type TableRowData } from 'tdesign-vue-next';
import { AddIcon, SearchIcon, RefreshIcon } from 'tdesign-icons-vue-next';

import { getUserList, createUser, updateUser, deleteUser } from '@/api/user';
import type { AdminUserDto } from '@/api/model/user';
import { useUserStore,useTunnelsStore,useInstanceListStore } from '@/store';

// --- 状态定义 ---
const loading = ref(false);
const tableData = ref<AdminUserDto[]>([]);
const filterText = ref('');

const userStore = useUserStore();
const tunnelsStore = useTunnelsStore();
const instanceStore = useInstanceListStore();

const dialogVisible = ref(false);
const dialogMode = ref<'create' | 'edit'>('create');
const submitLoading = ref(false);

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

// 资源分配选项
const resourceOptions = computed(() => {
  return [
    {
      label: '实例 (Server)',
      children: instanceStore.instanceList.map((item: any) => ({
        label: `[${item.id ?? item.ID}] ${item.name ?? item.Name}`,
        value: `server:${item.id ?? item.ID}`,
      })),
    },
    {
      label: '隧道 (FRP)',
      children: tunnelsStore.frpList.map((item: any) => ({
        label: `[${item.id ?? item.ID}] ${item.name ?? item.Name}`,
        value: `frp:${item.id ?? item.ID}`,
      })),
    },
  ];
});

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
  dialogVisible.value = true;
};

const onSubmit = async ({ validateResult }: any) => {
  if (validateResult !== true) return;

  submitLoading.value = true;
  try {
    if (dialogMode.value === 'create') {
      await createUser({
        username: formData.username,
        password: formData.password,
        name: formData.name,
        role: formData.role,
        resources: formData.role === 'admin' ? [] : formData.resources, // 管理员不需要分配，清空传过去
      });
      MessagePlugin.success('用户创建成功');
    } else {
      await updateUser(formData.id, {
        name: formData.name,
        password: formData.password || undefined,
        role: formData.role,
        resetApiKey: formData.resetApiKey,
        resources: formData.role === 'admin' ? [] : formData.resources, // 同上
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

onMounted(() => {
  fetchData();
  tunnelsStore.getTunnels();
  instanceStore.refreshInstanceList();
});
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-zinc-800 dark:text-zinc-200 pb-5">

    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left"
    >
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">用户管理</h2>
          <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">管理系统内的账户权限与实例资源分配</span>
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
      <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-5 sm:p-6" style="animation-delay: 0.05s;">

        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="text-base font-bold text-zinc-900 dark:text-zinc-100">用户列表</div>
          <div class="w-full sm:w-72">
            <t-input v-model="filterText" placeholder="搜索用户名或昵称" clearable>
              <template #prefix-icon><search-icon class="opacity-60" /></template>
            </t-input>
          </div>
        </div>

        <t-table
          row-key="id"
          :data="displayData"
          :columns="columns"
          :loading="loading"
          :hover="true"
          :pagination="{
            defaultPageSize: 20,
            total: displayData.length,
            showJumper: true,
          }"
          class="!bg-transparent"
          table-layout="auto"
        >
          <template #info-slot="{ row }">
            <div class="flex items-center gap-3 py-1">
              <t-avatar :image="row.avatar" size="44px" shape="circle" class="shrink-0 ring-2 ring-zinc-100 dark:ring-zinc-700/50 shadow-sm !bg-[var(--color-primary)]/10 !text-[var(--color-primary)]" :hide-on-load-failed="false">
                <span class="font-bold text-lg">{{ row.name ? row.name[0].toUpperCase() : 'U' }}</span>
              </t-avatar>
              <div class="flex flex-col min-w-0">
                <div class="font-bold text-sm text-zinc-900 dark:text-zinc-100 truncate">{{ row.name || '未设置昵称' }}</div>
                <div class="text-xs font-mono text-zinc-500 dark:text-zinc-400 mt-0.5 truncate">@{{ row.username }}</div>
              </div>
            </div>
          </template>

          <template #role-slot="{ row }">
            <span v-if="row.role === 'admin'" class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-success)]/10 text-[var(--color-success)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-success)]/20 shadow-sm">
              管理员
            </span>
            <span v-else class="inline-flex items-center px-2.5 py-1 rounded-md bg-[var(--color-primary)]/10 text-[var(--color-primary)] font-extrabold text-[11px] tracking-wider uppercase border border-[var(--color-primary)]/20 shadow-sm">
              普通用户
            </span>
          </template>

          <template #time-slot="{ row }">
            <div class="flex items-center gap-1.5">
              <time-icon v-if="row.lastLoginTime" class="text-[var(--color-primary)] opacity-70" size="14px" />
              <span v-if="row.lastLoginTime" class="text-xs font-mono font-medium text-zinc-600 dark:text-zinc-400">{{ new Date(row.lastLoginTime).toLocaleString() }}</span>
              <span v-else class="text-xs font-medium px-2 py-0.5 rounded-full bg-zinc-100 dark:bg-zinc-800 text-zinc-400 dark:text-zinc-500">从未登录</span>
            </div>
          </template>

          <template #op-slot="{ row }">
            <div class="flex items-center gap-1">
              <t-button variant="text" theme="primary" size="small" class="hover:!bg-[var(--color-primary)]/10" @click="handleEdit(row)">
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
                <template #message>资源分配仅实现基础权限隔离，实例文件系统并非物理隔离。用户可能通过程序路径穿越访问敏感数据，请仅在信任伙伴间使用，<strong style="color: var(--td-error-color);">严禁用于商业化用途</strong>。
                </template>
              </t-alert>
            </t-form-item>

          <t-form-item
            label="分配资源"
            name="resources"
          >
            <t-select
              v-model="formData.resources"
              multiple
              filterable
              clearable
              :options="resourceOptions"
              placeholder="搜索或选择要分配的实例与隧道"
            />
            <template #help>
              <span class="text-[11px] text-zinc-400 dark:text-zinc-500 mt-1 inline-block">该用户将获得以上选定实例和隧道的完整控制权</span>
            </template>
          </t-form-item>
        </template>

        <t-form-item label="密码设置" name="password">
          <t-input v-model="formData.password" type="password" placeholder="设置新密码" autocomplete="new-password" />
          <template #help>
            <span v-if="dialogMode === 'edit'" class="text-[11px] text-zinc-400 dark:text-zinc-500 mt-1 inline-block">留空则保持原密码不变</span>
          </template>
        </t-form-item>

        <t-form-item v-if="dialogMode === 'edit'" label="开发者选项">
          <div class="flex items-center gap-3 bg-zinc-50 dark:bg-zinc-800/50 p-3 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 w-full mt-1">
            <t-checkbox v-model="formData.resetApiKey">强制重置该用户的 API Key</t-checkbox>
          </div>
        </t-form-item>

      </t-form>
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

/* 兼容原有脚本中的隐藏类 */
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
