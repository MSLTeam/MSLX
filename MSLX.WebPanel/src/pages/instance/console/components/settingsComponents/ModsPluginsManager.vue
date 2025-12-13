<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import {
  MessagePlugin,
  DialogPlugin,
  type TableRowData,
  type PrimaryTableCol,
} from 'tdesign-vue-next';
import {
  SearchIcon,
  RefreshIcon,
  DeleteIcon,
  CheckCircleIcon,
  CloseCircleIcon,
  UploadIcon,
  AppIcon,
  ExtensionIcon
} from 'tdesign-icons-vue-next';

import { getPluginsOrModsList, setPluginsOrModsStatus } from '@/api/files';

import UploadDialog from '@/pages/instance/files/components/FileUploader.vue';

const route = useRoute();
const instanceId = parseInt(route.params.id as string);

// --- 类型定义 ---
interface FileItem {
  name: string;
  status: 'enabled' | 'disabled';
}

// --- 状态管理 ---
const mode = ref<'mods' | 'plugins'>('mods'); // 当前模式
const loading = ref(false);
const rawList = ref<FileItem[]>([]); // 原始数据
const filterText = ref(''); // 搜索关键词
const selectedRowKeys = ref<Array<string | number>>([]); // 多选选中项
const errorMsg = ref(''); // 新增：用于存储加载失败的错误信息

// --- 表格列定义 ---
const columns = computed<PrimaryTableCol<TableRowData>[]>(() => [
  { colKey: 'row-select', type: 'multiple', width: 50, fixed: 'left' },
  {
    title: '文件名',
    colKey: 'name',
    sorter: (a, b) => a.name.localeCompare(b.name),
    ellipsis: true,
  },
  {
    title: '状态',
    colKey: 'status',
    width: 100,
  },
  {
    title: '操作',
    colKey: 'op',
    width: 180,
    fixed: 'right',
    align: 'right'
  },
]);

// --- 计算属性：过滤后的列表 ---
const displayList = computed(() => {
  if (!filterText.value) return rawList.value;
  const key = filterText.value.toLowerCase();
  return rawList.value.filter(item => item.name.toLowerCase().includes(key));
});

// --- 核心逻辑：获取列表 ---
const fetchData = async () => {
  loading.value = true;
  selectedRowKeys.value = [];
  errorMsg.value = ''; // 每次请求前重置错误信息
  try {
    const res = await getPluginsOrModsList(instanceId, mode.value);

    // 数据转换
    const activeFiles = (res.jarFiles || []).map(name => ({ name, status: 'enabled' } as FileItem));
    const disabledFiles = (res.disableJarFiles || []).map(name => ({ name, status: 'disabled' } as FileItem));

    rawList.value = [...activeFiles, ...disabledFiles];

  } catch (e: any) {
    const msg = e.message || '获取列表失败';
    // 这里保留 MessagePlugin 提示，同时设置页面内的 Alert
    // MessagePlugin.error(msg);
    errorMsg.value = msg; // 设置错误信息到变量
    rawList.value = [];
  } finally {
    loading.value = false;
  }
};

// --- 操作 ---
const handleAction = async (action: 'enable' | 'disable' | 'delete', targets: string[]) => {
  if (targets.length === 0) return;

  const actionMap = {
    enable: '启用',
    disable: '禁用',
    delete: '删除'
  };

  const confirmAction = async () => {
    try {
      loading.value = true;
      const res = await setPluginsOrModsStatus(instanceId, mode.value, action, targets);
      const { successCount, failCount } = res || {};

      if (failCount > 0) {
        MessagePlugin.warning(`操作完成：成功 ${successCount} 个，失败 ${failCount} 个`);
      } else {
        MessagePlugin.success(`成功${actionMap[action]} ${successCount} 个文件`);
      }

      await fetchData(); // 刷新列表
    } catch (e: any) {
      MessagePlugin.error(e.message || '操作失败');
      loading.value = false;
    }
  };

  // 二次确认
  if (action === 'delete') {
    const confirmDialog = DialogPlugin.confirm({
      header: `确认删除 ${targets.length} 个文件?`,
      body: '此操作不可逆，文件将被永久删除。',
      theme: 'danger',
      onConfirm: () => {
        confirmDialog.hide();
        confirmAction();
      },
    });
  } else {
    // 启用/禁用直接执行
    await confirmAction();
  }
};

// --- 事件 ---

// 切换 Tabs
const onModeChange = (val: string | number) => {
  mode.value = val as 'mods' | 'plugins';
  fetchData();
};

// 单行切换状态
const toggleStatus = (row: FileItem) => {
  const newAction = row.status === 'enabled' ? 'disable' : 'enable';
  handleAction(newAction, [row.name]);
};

// 批量操作
const handleBulk = (action: 'enable' | 'disable' | 'delete') => {
  const targets = selectedRowKeys.value as string[];
  handleAction(action, targets);
};

// 上传逻辑
const showUploadDialog = ref(false);
const handleUpload = () => {
  showUploadDialog.value = true;
};
const onUploadSuccess = () => {
  showUploadDialog.value = false; // 关闭弹窗
  fetchData(); // 刷新列表
  // MessagePlugin.success('文件已上传!');
};

// --- 生命周期 ---
onMounted(() => {
  fetchData();
});

watch(() => route.params.id, (newId) => {
  if(newId) location.reload();
});
</script>

<template>
  <div class="pm-container">

    <div class="header-section">
      <div class="setting-group-title">
        资源管理
      </div>
      <div class="mode-switcher">
        <t-radio-group v-model="mode" variant="default-filled" @change="onModeChange">
          <t-radio-button value="mods">
            <template #icon><extension-icon /></template>
            模组 (Mods)
          </t-radio-button>
          <t-radio-button value="plugins">
            <template #icon><app-icon /></template>
            插件 (Plugins)
          </t-radio-button>
        </t-radio-group>
      </div>
    </div>

    <div v-if="errorMsg" class="error-alert-bar">
      <t-alert theme="error" :message="errorMsg" closeable @close="errorMsg = ''">
        <template #operation>
          <span style="cursor: pointer; margin-left: 8px" @click="fetchData">重试</span>
        </template>
      </t-alert>
    </div>

    <div class="toolbar">
      <div class="left-actions">
        <t-button theme="primary" :disabled="errorMsg !== ''" @click="handleUpload">
          <template #icon><upload-icon /></template>
          上传文件
        </t-button>
        <t-button variant="outline" :loading="loading" @click="fetchData">
          <template #icon><refresh-icon /></template>
        </t-button>

        <transition name="fade">
          <div v-if="selectedRowKeys.length > 0" class="bulk-actions">
            <div class="divider"></div>
            <span class="selected-count">已选 {{ selectedRowKeys.length }} 项</span>
            <t-button size="small" variant="text" theme="success" @click="handleBulk('enable')">启用</t-button>
            <t-button size="small" variant="text" theme="warning" @click="handleBulk('disable')">禁用</t-button>
            <t-button size="small" variant="text" theme="danger" @click="handleBulk('delete')">删除</t-button>
          </div>
        </transition>
      </div>

      <div class="right-search">
        <t-input v-model="filterText" placeholder="搜索文件名..." clearable>
          <template #prefix-icon><search-icon /></template>
        </t-input>
      </div>
    </div>

    <div class="table-wrapper">
      <t-table
        v-model:selected-row-keys="selectedRowKeys"
        :data="displayList"
        :columns="columns"
        :row-key="'name'"
        :loading="loading"
        :pagination="{ defaultPageSize: 20, total: displayList.length, showJumper: true }"
        hover
        stripe
        class="custom-table"
      >
        <template #status="{ row }">
          <t-tag v-if="row.status === 'enabled'" theme="success" variant="light">
            <template #icon><check-circle-icon /></template>
            已启用
          </t-tag>
          <t-tag v-else theme="default" variant="light" style="color: var(--td-text-color-placeholder)">
            <template #icon><close-circle-icon /></template>
            已禁用
          </t-tag>
        </template>

        <template #op="{ row }">
          <t-space size="small">
            <t-switch
              :model-value="row.status === 'enabled'"
              :loading="loading"
              size="medium"
              @change="toggleStatus(row)"
            >
              <template #label="slotProps">{{ slotProps.value ? '开' : '关' }}</template>
            </t-switch>

            <t-popconfirm content="确定要删除此文件吗？" theme="danger" @confirm="handleAction('delete', [row.name])">
              <t-button variant="text" theme="danger" shape="square">
                <delete-icon />
              </t-button>
            </t-popconfirm>
          </t-space>
        </template>

        <template #empty>
          <div class="empty-state">
            <extension-icon v-if="mode === 'mods'" size="48px" style="color: var(--td-gray-color-4)" />
            <app-icon v-else size="48px" style="color: var(--td-gray-color-4)" />
            <p>{{ `暂无${mode === 'mods' ? '模组' : '插件'}文件` }}</p>
          </div>
        </template>
      </t-table>
    </div>

    <div class="tips-footer">
      <p>提示：{{ mode === 'mods' ? '模组' : '插件' }}文件存放于服务器根目录的 /{{ mode }} 文件夹下。</p>
      <p>禁用文件后，会在文件名后添加 .disabled 后缀，服务器将忽略该文件。</p>
    </div>

    <upload-dialog
      v-model:visible="showUploadDialog"
      :instance-id="instanceId"
      :current-path="mode"
      :allow-folder="false"
      @success="onUploadSuccess"
    />

  </div>
</template>

<style scoped lang="less">
.pm-container {
  /* 保持与参考页面一致的背景处理，如果外部有 layout 这里可以去掉 padding */
  padding-bottom: 24px;
}

/* 标题样式复刻 */
.setting-group-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  display: flex;
  align-items: center;

  &::before {
    content: '';
    display: inline-block;
    width: 4px;
    height: 16px;
    background-color: var(--td-brand-color);
    margin-right: 8px;
    border-radius: 2px;
  }
}

.header-section {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 24px;
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);
}

.error-alert-bar {
  margin-bottom: 16px;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;

  .left-actions {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .right-search {
    width: 240px;
  }
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  background-color: var(--td-bg-color-container-hover);
  padding: 4px 12px;
  border-radius: var(--td-radius-medium);

  .divider {
    width: 1px;
    height: 14px;
    background-color: var(--td-component-stroke);
    margin: 0 4px;
  }

  .selected-count {
    font-size: 12px;
    color: var(--td-text-color-secondary);
    margin-right: 4px;
  }
}

.table-wrapper {
  background-color: var(--td-bg-color-container);
  border-radius: var(--td-radius-medium);
  overflow: hidden; /* 圆角溢出处理 */
  border: 1px solid var(--td-component-stroke);
}

.empty-state {
  padding: 48px 0;
  text-align: center;
  color: var(--td-text-color-secondary);
  p {
    margin-top: 12px;
    font-size: 14px;
  }
}

.tips-footer {
  margin-top: 16px;
  font-size: 12px;
  color: var(--td-text-color-placeholder);
  line-height: 20px;
}

/* 响应式适配 */
@media (max-width: 768px) {
  .header-section {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;

    .setting-group-title {
      width: 100%;
      border-bottom: none;
      margin-bottom: 0;
      padding-bottom: 0;
    }

    .mode-switcher {
      width: 100%;
      :deep(.t-radio-group) {
        width: 100%;
        display: flex;
        .t-radio-button {
          flex: 1;
          text-align: center;
        }
      }
    }
  }

  .toolbar {
    flex-direction: column-reverse; /* 手机上搜索框放上面可能更好，这里保持原逻辑 */
    align-items: stretch;

    .right-search {
      width: 100%;
    }

    .left-actions {
      justify-content: space-between;
      width: 100%;
    }
  }

  .bulk-actions {
    flex: 1; /* 在移动端让批量操作栏占据剩余空间 */
    justify-content: flex-end;
  }
}

/* 动画效果 */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
