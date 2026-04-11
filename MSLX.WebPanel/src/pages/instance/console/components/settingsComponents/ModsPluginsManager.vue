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
const instanceId = parseInt(route.params.serverId as string);

// --- 类型定义 ---
interface FileItem {
  name: string;
  status: 'enabled' | 'disabled';
  isClient?: boolean;
}



// --- 状态管理 ---
const mode = ref<'mods' | 'plugins'>('mods'); // 当前模式
const loading = ref(false);
const rawList = ref<FileItem[]>([]); // 原始数据
const filterText = ref(''); // 搜索关键词
const selectedRowKeys = ref<Array<string | number>>([]); // 多选选中项
const errorMsg = ref(''); // 用于存储加载失败的错误信息

// --- 计算属性：过滤后的列表 ---
const displayList = computed(() => {
  if (!filterText.value) return rawList.value;
  const key = filterText.value.toLowerCase();
  return rawList.value.filter(item => item.name.toLowerCase().includes(key));
});


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

// --- 核心逻辑：获取列表 ---
const fetchData = async (checkClient = false) => {
  loading.value = true;
  selectedRowKeys.value = [];
  errorMsg.value = ''; // 每次请求前重置错误信息
  try {
    const res = await getPluginsOrModsList(instanceId, mode.value, checkClient);

    // 客户端模组
    const clientFiles = (res.clientJarFiles || []).map((name: string) => ({
      name,
      status: 'enabled',
      isClient: true
    } as FileItem));

    // 数据转换
    const activeFiles = (res.jarFiles || []).map(name => ({ name, status: 'enabled' } as FileItem));
    const disabledFiles = (res.disableJarFiles || []).map(name => ({ name, status: 'disabled' } as FileItem));

    rawList.value = [...clientFiles, ...activeFiles, ...disabledFiles];

    // 如果检测到了客户端模组，给个提示
    if (checkClient && clientFiles.length > 0) {
      // 自动勾选
      selectedRowKeys.value = clientFiles.map(item => item.name);

      MessagePlugin.success(`检测到 ${clientFiles.length} 个客户端模组`);
    } else if (checkClient) {
      MessagePlugin.info('未检测到仅客户端模组');
    }

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

watch(() => route.params.serverId, (newId) => {
  if(newId) location.reload();
});
</script>

<template>
  <div class="flex flex-col pb-6">

    <div class="flex flex-col md:flex-row md:items-center justify-between gap-4 mt-5 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60">
      <div class="flex items-center gap-2">
        <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
        <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">资源管理</h2>
      </div>

      <div class="w-full md:w-auto">
        <t-radio-group v-model="mode" variant="default-filled" class="!bg-zinc-100 dark:!bg-zinc-800 border border-[var(--td-component-border)] !rounded-lg p-0.5 shadow-sm flex w-full" @change="onModeChange">
          <t-radio-button value="mods" class="flex-1 md:flex-none !text-center">
            <div class="flex justify-center items-center gap-1.5"><extension-icon size="14px"/> 模组 (Mods)</div>
          </t-radio-button>
          <t-radio-button value="plugins" class="flex-1 md:flex-none !text-center">
            <div class="flex justify-center items-center gap-1.5"><app-icon size="14px"/> 插件 (Plugins)</div>
          </t-radio-button>
        </t-radio-group>
      </div>
    </div>

    <div v-if="errorMsg" class="mb-4">
      <t-alert theme="error" :message="errorMsg" closeable class="!rounded-xl shadow-sm border border-red-100 dark:border-red-900/50" @close="errorMsg = ''">
        <template #operation>
          <span class="cursor-pointer ml-2 font-bold text-red-600 dark:text-red-400 hover:opacity-80 transition-opacity" @click="fetchData(false)">重试</span>
        </template>
      </t-alert>
    </div>

    <div class="flex flex-col-reverse md:flex-row justify-between items-stretch md:items-center gap-3 mb-4">

      <div class="flex flex-wrap items-center gap-2 w-full md:w-auto justify-between md:justify-start">
        <div class="flex items-center gap-2">
          <t-button theme="primary" class="!rounded-lg shadow-sm" :disabled="errorMsg !== ''" @click="handleUpload">
            <template #icon><upload-icon /></template> 上传文件
          </t-button>

          <t-button v-if="mode === 'mods'" variant="outline" class="!rounded-lg !bg-zinc-50 dark:!bg-zinc-800/50 !border-zinc-200 dark:!border-zinc-700 hover:!bg-zinc-100 dark:hover:!bg-zinc-800" :disabled="errorMsg !== ''" :loading="loading" @click="fetchData(true)">
            <template #icon><search-icon /></template> 检测客户端模组
          </t-button>

          <t-button variant="outline" class="!rounded-lg !bg-zinc-50 dark:!bg-zinc-800/50 !border-zinc-200 dark:!border-zinc-700 hover:!bg-zinc-100 dark:hover:!bg-zinc-800 shrink-0" :loading="loading" @click="fetchData(false)">
            <template #icon><refresh-icon /></template>
          </t-button>
        </div>

        <transition
          enter-active-class="transition-opacity duration-200 ease-out"
          enter-from-class="opacity-0"
          leave-active-class="transition-opacity duration-200 ease-in"
          leave-to-class="opacity-0"
        >
          <div v-if="selectedRowKeys.length > 0" class="flex items-center bg-zinc-100 dark:bg-zinc-800/80 px-3 py-1.5 rounded-lg gap-3 shadow-sm flex-1 md:flex-none justify-end md:justify-start">
            <div class="hidden md:block w-[2px] h-[14px] bg-zinc-300 dark:bg-zinc-600 -mr-1"></div>
            <span class="text-xs font-bold text-zinc-600 dark:text-zinc-300 shrink-0">已选 {{ selectedRowKeys.length }} 项</span>
            <div class="flex items-center">
              <t-button size="small" variant="text" theme="success" class="!px-2 !h-7 !text-xs hover:!bg-emerald-500/10 !rounded-md" @click="handleBulk('enable')">启用</t-button>
              <t-button size="small" variant="text" theme="warning" class="!px-2 !h-7 !text-xs hover:!bg-amber-500/10 !rounded-md" @click="handleBulk('disable')">禁用</t-button>
              <t-button size="small" variant="text" theme="danger" class="!px-2 !h-7 !text-xs hover:!bg-red-500/10 !rounded-md" @click="handleBulk('delete')">删除</t-button>
            </div>
          </div>
        </transition>
      </div>

      <div class="w-full md:w-60 shrink-0">
        <t-input v-model="filterText" placeholder="搜索文件名..." clearable class="!w-full !rounded-lg shadow-sm">
          <template #prefix-icon><search-icon class="text-zinc-400" /></template>
        </t-input>
      </div>
    </div>

    <div class="border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl overflow-hidden shadow-sm bg-white/50 dark:bg-zinc-900/20">
      <t-table
        v-model:selected-row-keys="selectedRowKeys"
        :pagination="{ defaultPageSize: 20, total: displayList.length, showJumper: true, defaultCurrent: 1 }"
        :data="displayList"
        :columns="columns"
        :row-key="'name'"
        :loading="loading"
        hover
        stripe
        class="custom-table"
      >
        <template #name="{ row }">
          <div class="flex items-center gap-2 font-mono text-[13px] text-zinc-700 dark:text-zinc-300 break-all">
            <span>{{ row.name }}</span>
            <t-tag v-if="row.isClient" theme="warning" variant="light" size="small" class="!rounded shrink-0">
              客户端
            </t-tag>
          </div>
        </template>

        <template #status="{ row }">
          <t-tag v-if="row.status === 'enabled'" theme="success" variant="light" class="!rounded-md">
            <template #icon><check-circle-icon /></template>启用
          </t-tag>
          <t-tag v-else theme="default" variant="light" class="!rounded-md !text-zinc-500 dark:!text-zinc-400">
            <template #icon><close-circle-icon /></template>禁用
          </t-tag>
        </template>

        <template #op="{ row }">
          <t-space size="small" class="flex items-center">
            <t-switch
              :model-value="row.status === 'enabled'"
              :loading="loading"
              size="medium"
              @change="toggleStatus(row)"
            >
            </t-switch>

            <t-popconfirm content="确定要删除此文件吗？" theme="danger" @confirm="handleAction('delete', [row.name])">
              <t-button variant="text" theme="danger" shape="square" class="!rounded-md hover:!bg-red-500/10 transition-colors">
                <delete-icon />
              </t-button>
            </t-popconfirm>
          </t-space>
        </template>

        <template #empty>
          <div class="py-16 flex flex-col items-center justify-center text-[var(--td-text-color-secondary)]">
            <extension-icon v-if="mode === 'mods'" size="40px" class="opacity-60 mb-3" />
            <app-icon v-else size="40px" class="opacity-60 mb-3" />
            <span class="text-sm font-medium">暂无{{ mode === 'mods' ? '模组' : '插件' }}文件</span>
          </div>
        </template>
      </t-table>
    </div>

    <div class="mt-4 text-[11px] text-[var(--td-text-color-secondary)] leading-relaxed tracking-wider space-y-1">
      <p>提示：{{ mode === 'mods' ? '模组' : '插件' }}文件存放于服务器根目录的 /{{ mode }} 文件夹下。</p>
      <p>禁用文件后，会在文件名后添加 <code class="bg-zinc-100 dark:bg-zinc-800 px-1 py-0.5 rounded font-mono text-[10px] text-[var(--td-text-color-secondary)]">.disabled</code> 后缀，服务器将自动忽略该文件。</p>
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
@reference "@/style/tailwind/index.css";
</style>
