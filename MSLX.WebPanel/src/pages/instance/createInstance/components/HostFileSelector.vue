<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  DesktopIcon,
  FileIcon,
  FileZipIcon,
  FilterIcon,
  FolderIcon,
  RollbackIcon,
  SearchIcon,
} from 'tdesign-icons-vue-next';
import { getHostDrivesList, getHostFilesList } from '@/api/files';
import { HostDriveItem, HostFileItem } from '@/api/model/files';

const props = defineProps({
  visible: { type: Boolean, default: false },
  defaultPath: { type: String, default: '' },
  searchPattern: { type: String, default: '*.zip' },
});

const emit = defineEmits(['update:visible', 'select']);

// --- 状态管理 ---
const isVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val),
});

const loading = ref(false);
const viewMode = ref<'drives' | 'files'>('files');
const currentPath = ref('');
const parentPath = ref<string | null>(null);

const fileList = ref<HostFileItem[]>([]);
const drivesList = ref<HostDriveItem[]>([]);
const selectedRowKeys = ref<string[]>([]);

const searchKey = ref('');
const sortType = ref('name');

const sortOptions = [
  { label: '名称 (A-Z)', value: 'name' },
  { label: '时间 (最新)', value: 'time' },
  { label: '大小 (从大到小)', value: 'size' },
];

// --- 计算属性 ---
const filteredFileList = computed(() => {
  let list = [...fileList.value];
  if (searchKey.value) {
    const key = searchKey.value.toLowerCase();
    list = list.filter((item) => item.name.toLowerCase().includes(key));
  }
  list.sort((a, b) => {
    if (a.type === 'folder' && b.type !== 'folder') return -1;
    if (a.type !== 'folder' && b.type === 'folder') return 1;
    switch (sortType.value) {
      case 'name':
        return a.name.localeCompare(b.name, 'zh-CN', { numeric: true });
      case 'time':
        return new Date(b.lastModified).getTime() - new Date(a.lastModified).getTime();
      case 'size':
        return b.size - a.size;
      default:
        return 0;
    }
  });
  return list;
});

const unifiedData = computed(() => {
  if (viewMode.value === 'drives') {
    return drivesList.value.map((d) => ({ ...d, key: d.path, isDrive: true }));
  }
  return filteredFileList.value.map((f) => ({ ...f, key: f.path, isDrive: false }));
});

const breadcrumbs = computed(() => {
  const crumbs = [{ name: '我的电脑', path: 'ROOT', isRoot: true }];
  if (viewMode.value === 'drives' || !currentPath.value) return crumbs;

  // 分隔符
  const parts = currentPath.value.split(/[/\\]/).filter((p) => p);
  let accumPath = currentPath.value.startsWith('/') ? '' : ''; // Linux 根目录处理

  parts.forEach((part, index) => {
    // unix 根目录前缀
    if (index === 0 && currentPath.value.startsWith('/')) {
      accumPath = `/${part}`;
    } else {
      accumPath = accumPath ? `${accumPath}${currentPath.value.includes('\\') ? '\\' : '/'}${part}` : part;
    }
    crumbs.push({ name: part, path: accumPath, isRoot: false });
  });
  return crumbs;
});

const columns = computed(() => {
  // 禁用盘符和文件夹选择
  const selectionCol = {
    colKey: 'row-select',
    type: 'single',
    width: 40,
    disabled: (params: any) => params.row.isDrive || params.row.type === 'folder',
  };
  const nameCol = { colKey: 'name', title: '名称', ellipsis: true, width: 'auto' };

  if (viewMode.value === 'drives') {
    return [
      nameCol,
      { colKey: 'volumeLabel', title: '卷标', width: 120 },
      { colKey: 'availableFreeSpace', title: '可用空间', width: 120, align: 'right' },
    ];
  }

  return [
    selectionCol,
    nameCol,
    { colKey: 'size', title: '大小', width: 100, align: 'right' },
    { colKey: 'lastModified', title: '修改时间', width: 160, align: 'center' },
  ];
});

// --- 数据获取 ---
const fetchDrives = async () => {
  loading.value = true;
  try {
    drivesList.value = await getHostDrivesList();
    viewMode.value = 'drives';
    currentPath.value = '';
    parentPath.value = null;
    selectedRowKeys.value = [];
  } catch (err: any) {
    MessagePlugin.error('获取磁盘列表失败:' + err.msg);
  } finally {
    loading.value = false;
  }
};

const fetchFiles = async (path?: string) => {
  loading.value = true;
  try {
    const res = await getHostFilesList(path, props.searchPattern);
    currentPath.value = res.currentPath;
    parentPath.value = res.parentPath;
    fileList.value = res.items;
    viewMode.value = 'files';
    selectedRowKeys.value = [];
  } catch (error: any) {
    MessagePlugin.error(error.message || '获取目录失败，可能是权限不足');
    // 如果获取失败且当前在根目录之下，尝试回退到磁盘列表
    if (!parentPath.value) fetchDrives();
  } finally {
    loading.value = false;
  }
};

// --- 操作逻辑 ---
const handleRowClick = (row: any) => {
  if (row.isDrive || row.type === 'folder') {
    fetchFiles(row.path);
  } else {
    selectedRowKeys.value = [row.key];
  }
};

const navigateTo = (path: string) => {
  if (path === 'ROOT') fetchDrives();
  else fetchFiles(path);
};

const goBack = () => {
  if (viewMode.value === 'drives') return;
  if (parentPath.value) fetchFiles(parentPath.value);
  else fetchDrives();
};

const handleConfirm = () => {
  if (selectedRowKeys.value.length === 0) {
    MessagePlugin.warning('请先选择一个文件');
    return;
  }
  emit('select', selectedRowKeys.value[0]);
  isVisible.value = false;
};

// --- 工具函数 ---
const formatSize = (size: number) => {
  if (!size || size === 0) return '-';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (size >= 1024 && i < units.length - 1) {
    size /= 1024;
    i++;
  }
  return `${size.toFixed(1)} ${units[i]}`;
};

const formatTime = (timeStr: string) => {
  if (!timeStr) return '-';
  return new Date(timeStr).toLocaleString();
};

const getIcon = (row: any) => {
  if (row.isDrive) return { icon: DesktopIcon, color: 'var(--td-brand-color)' };
  if (row.type === 'folder') return { icon: FolderIcon, color: 'var(--td-brand-color)' };
  if (row.name.toLowerCase().endsWith('.zip')) return { icon: FileZipIcon, color: '#722ed1' };
  return { icon: FileIcon, color: 'var(--td-text-color-secondary)' };
};

// --- 生命周期 ---
watch(isVisible, (val) => {
  if (val) {
    if (props.defaultPath) fetchFiles(props.defaultPath);
    else fetchFiles();
  }
});
</script>

<template>
  <t-dialog
    v-model:visible="isVisible"
    header="选择本机文件"
    width="850px"
    placement="center"
    :confirm-btn="{ content: '确认选择', disabled: selectedRowKeys.length === 0 }"
    @confirm="handleConfirm"
  >
    <div class="flex flex-col h-[60vh] min-h-[450px] -mx-6 -my-4 bg-zinc-50 dark:bg-[#1e1e1e]">
      <div
        class="px-5 py-3 bg-white dark:bg-[#242424] border-b border-zinc-200 dark:border-zinc-800 flex items-center justify-between gap-4"
      >
        <div class="flex items-center gap-2 overflow-hidden flex-1">
          <t-button variant="text" shape="square" :disabled="viewMode === 'drives'" @click="goBack">
            <rollback-icon />
          </t-button>
          <div class="flex-1 overflow-x-auto hide-scrollbar flex items-center">
            <t-breadcrumb max-item-width="150px">
              <t-breadcrumb-item
                v-for="(crumb, index) in breadcrumbs"
                :key="index"
                class="cursor-pointer whitespace-nowrap"
                @click="navigateTo(crumb.path)"
              >
                <template v-if="crumb.isRoot" #icon><desktop-icon /></template>
                {{ crumb.name }}
              </t-breadcrumb-item>
            </t-breadcrumb>
          </div>
        </div>

        <div class="flex items-center gap-2 shrink-0" v-show="viewMode === 'files'">
          <t-input v-model="searchKey" placeholder="搜索文件..." class="!w-36">
            <template #prefix-icon><search-icon /></template>
          </t-input>
          <t-select v-model="sortType" :options="sortOptions" class="!w-32" placeholder="排序">
            <template #prefixIcon><filter-icon /></template>
          </t-select>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto bg-white dark:bg-[#242424]">
        <t-table
          v-model:selected-row-keys="selectedRowKeys"
          :data="unifiedData"
          :columns="columns as any"
          row-key="key"
          :loading="loading"
          :hover="true"
          size="medium"
          class="custom-table cursor-pointer [&_.t-table\_\_header]:sticky [&_.t-table\_\_header]:top-0 [&_.t-table\_\_header]:z-10"
          @row-click="({ row }) => handleRowClick(row)"
        >
          <template #name="{ row }">
            <div class="flex items-center py-1 group">
              <component :is="getIcon(row).icon" class="text-xl mr-2 shrink-0" :style="{ color: getIcon(row).color }" />
              <span class="font-medium text-[var(--td-text-color-primary)] truncate">
                {{ row.name }}
              </span>
            </div>
          </template>
          <template #volumeLabel="{ row }">{{ row.volumeLabel || '-' }}</template>
          <template #availableFreeSpace="{ row }">
            <span class="text-[13px] font-mono text-[var(--td-text-color-secondary)]">{{
              formatSize(row.availableFreeSpace)
            }}</span>
          </template>
          <template #size="{ row }">
            <span class="text-[13px] font-mono text-[var(--td-text-color-secondary)]">{{ formatSize(row.size) }}</span>
          </template>
          <template #lastModified="{ row }">
            <span class="text-[13px] text-[var(--td-text-color-secondary)]">{{ formatTime(row.lastModified) }}</span>
          </template>

          <template #empty>
            <div class="py-12 flex flex-col items-center justify-center text-[var(--td-text-color-secondary)]">
              <file-icon size="32px" class="opacity-40 mb-2" />
              <span class="text-sm">空目录</span>
            </div>
          </template>
        </t-table>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped>
.hide-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
}
.hide-scrollbar::-webkit-scrollbar {
  display: none;
}
:deep(.t-breadcrumb__item.light) {
  background-color: transparent !important;
  color: unset !important;
}
</style>
