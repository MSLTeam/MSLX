<script setup lang="ts">
import { onMounted, reactive, ref, computed } from 'vue';
import {
  MessagePlugin,
  DialogPlugin,
  type FormRules,
  type PrimaryTableCol,
  type TableRowData
} from 'tdesign-vue-next';
import {
  AddIcon,
  SearchIcon,
  RefreshIcon
} from 'tdesign-icons-vue-next';

import { getUserList, createUser, updateUser, deleteUser } from '@/api/user';
import type { AdminUserDto } from '@/api/model/user';
import { useUserStore } from '@/store';

// --- 状态定义 ---
const loading = ref(false);
const tableData = ref<AdminUserDto[]>([]);
const filterText = ref('');
const userStore = useUserStore();

const dialogVisible = ref(false);
const dialogMode = ref<'create' | 'edit'>('create');
const submitLoading = ref(false);

const formData = reactive({
  id: '',
  username: '',
  name: '',
  password: '',
  role: 'user',
  resetApiKey: false
});

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', type: 'error' }],
  role: [{ required: true, message: '请选择角色', type: 'error' }],
  password: [{ validator: (val) => {
      if (dialogMode.value === 'create' && !val) return { result: false, message: '创建用户时密码必填', type: 'error' };
      return true;
    } }]
};

// 表格列定义
const columns = computed((): PrimaryTableCol<TableRowData>[] => [
  { colKey: 'info', title: '用户信息', width: 200, fixed: 'left', cell: 'info-slot' },
  { colKey: 'role', title: '角色', width: 100, cell: 'role-slot' },
  { colKey: 'lastLogin', title: '最后登录', width: 180, cell: 'time-slot', className: 'hidden-xs' },
  { colKey: 'op', title: '操作', width: 140, fixed: 'right', cell: 'op-slot' }
]);

// 过滤数据
const displayData = computed(() => {
  if (!filterText.value) return tableData.value;
  const key = filterText.value.toLowerCase();
  return tableData.value.filter(item =>
    item.username.toLowerCase().includes(key) ||
    (item.name && item.name.toLowerCase().includes(key))
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
        role: formData.role
      });
      MessagePlugin.success('用户创建成功');
    } else {
      await updateUser(formData.id, {
        name: formData.name,
        password: formData.password || undefined,
        role: formData.role,
        resetApiKey: formData.resetApiKey
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
    }
  });
};

onMounted(() => {
  fetchData();
});
</script>

<template>
  <div class="user-manage">
    <t-card :bordered="false" class="main-card">

      <div class="toolbar">
        <div class="left-action">
          <t-button theme="primary" @click="handleAdd">
            <template #icon><add-icon /></template>
            新增用户
          </t-button>
        </div>
        <div class="right-search">
          <t-input v-model="filterText" placeholder="搜索用户名或昵称" clearable>
            <template #prefix-icon><search-icon /></template>
          </t-input>
          <t-button variant="text" shape="square" @click="fetchData">
            <template #icon><refresh-icon /></template>
          </t-button>
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
          showJumper: true
        }"
        class="user-table"
        table-layout="auto"
      >
        <template #info-slot="{ row }">
          <div class="user-info-cell">
            <t-avatar :image="row.avatar" size="40px" shape="circle" :hide-on-load-failed="false">
              {{ row.name ? row.name[0].toUpperCase() : 'U' }}
            </t-avatar>
            <div class="text-info">
              <div class="nickname">{{ row.name || '未设置昵称' }}</div>
              <div class="username">@{{ row.username }}</div>
            </div>
          </div>
        </template>

        <template #role-slot="{ row }">
          <t-tag v-if="row.role === 'admin'" theme="success" variant="light" size="small" shape="round">管理员</t-tag>
          <t-tag v-else theme="primary" variant="light" size="small" shape="round">普通用户</t-tag>
        </template>

        <template #time-slot="{ row }">
          <span v-if="row.lastLoginTime" class="time-text">{{ new Date(row.lastLoginTime).toLocaleString() }}</span>
          <span v-else class="time-placeholder">从未登录</span>
        </template>

        <template #op-slot="{ row }">
          <div class="op-buttons">
            <t-button variant="text" theme="primary" size="small" @click="handleEdit(row)">
              编辑
            </t-button>
            <t-divider layout="vertical" />
            <t-button
              variant="text"
              theme="danger"
              size="small"
              :disabled="row.id === userStore.userInfo.id || row.username === 'admin'"
              @click="handleDelete(row)"
            >
              删除
            </t-button>
          </div>
        </template>
      </t-table>
    </t-card>

    <t-dialog
      v-model:visible="dialogVisible"
      :header="dialogMode === 'create' ? '新增用户' : '编辑用户'"
      :confirm-btn="{ content: '提交', loading: submitLoading }"
      :on-confirm="() => ($refs.formRef as any).submit()"
      width="480px"
    >
      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" @submit="onSubmit">

        <t-form-item label="用户名" name="username">
          <t-input
            v-model="formData.username"
            placeholder="登录账号"
            :disabled="dialogMode === 'edit'"
          />
        </t-form-item>

        <t-form-item label="昵称" name="name">
          <t-input v-model="formData.name" placeholder="显示名称" />
        </t-form-item>

        <t-form-item label="角色" name="role">
          <t-radio-group v-model="formData.role" variant="default-filled">
            <t-radio-button value="user" disabled>普通用户</t-radio-button>
            <t-radio-button value="admin">管理员</t-radio-button>
          </t-radio-group>
        </t-form-item>

        <t-form-item
          label="密码"
          name="password"
          :help="dialogMode === 'edit' ? '留空则不修改密码' : ''"
        >
          <t-input
            v-model="formData.password"
            type="password"
            placeholder="设置密码"
            autocomplete="new-password"
          />
        </t-form-item>

        <t-form-item v-if="dialogMode === 'edit'" label="API 密钥">
          <t-checkbox v-model="formData.resetApiKey">重置该用户的 API Key</t-checkbox>
        </t-form-item>

      </t-form>
    </t-dialog>
  </div>
</template>

<style lang="less" scoped>
.user-manage {
  padding: 0;
  @media (min-width: 768px) {
    // padding: 24px;
  }
}

.main-card {
  border-radius: 8px;
  overflow: hidden;
  min-height: calc(100vh - 120px);
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;

  .right-search {
    display: flex;
    gap: 8px;
    flex: 1;
    min-width: 200px;
    max-width: 300px;

    @media (max-width: 600px) {
      max-width: 100%;
      min-width: 100%;
    }
  }
}

.user-info-cell {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 4px 0;

  .text-info {
    display: flex;
    flex-direction: column;
    line-height: 1.4;

    .nickname {
      font-weight: 500;
      color: var(--td-text-color-primary);
    }

    .username {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      font-family: monospace;
    }
  }
}

.time-text {
  font-size: 13px;
  color: var(--td-text-color-secondary);
}
.time-placeholder {
  font-size: 13px;
  color: var(--td-text-color-disabled);
}

:deep(.hidden-xs) {
  @media (max-width: 768px) {
    display: none;
  }
}

:deep(.t-table__content) {
  scrollbar-width: thin;
}

.op-buttons {
  display: flex;
  align-items: center;
  gap: 4px;
}
</style>
