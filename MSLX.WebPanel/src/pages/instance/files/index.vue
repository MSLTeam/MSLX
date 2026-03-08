<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  AppIcon,
  CloudUploadIcon,
  CodeIcon,
  DeleteIcon,
  DownloadIcon,
  EarthIcon,
  FileAddIcon,
  FileIcon,
  FileImageIcon,
  FilePasteIcon,
  FileZipIcon,
  FolderIcon,
  HomeIcon,
  LockOnIcon,
  MoreIcon,
  RefreshIcon,
  RollbackIcon,
  SettingIcon,
  FileCopyIcon,
  SwapIcon,
  CloseIcon,
  SearchIcon,
  FilterIcon,
  EditIcon,
} from 'tdesign-icons-vue-next';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';
import {
  copyFiles,
  createDirectory,
  deleteFiles,
  downloadFileStream,
  getFileContent,
  getInstanceFilesList,
  moveFiles,
  renameFile,
  saveFileContent,
} from '@/api/files';
import type { FilesListModel } from '@/api/model/files';
import FileEditor from './components/FileEditor.vue';
import FileUploader from './components/FileUploader.vue';
import ImagePreview from './components/ImagePreview.vue';
import FileCompressor from './components/FileCompressor.vue';
import FileDecompress from './components/FileDecompress.vue';
import FilePermission from './components/FilePermission.vue';
import { changeUrl } from '@/router';
import { useUserStore } from '@/store';

const route = useRoute();
const router = useRouter();
const instanceId = computed(() => Number(route.params.serverFilesId));

const userStore = useUserStore();

const loading = ref(false);
const fileList = ref<FilesListModel[]>([]);
const currentPath = ref('');
const selectedRowKeys = ref<string[]>([]);

// 屏幕响应式状态
const screenWidth = ref(window.innerWidth);
const isMobile = computed(() => screenWidth.value < 768);

// 各类弹窗状态
const showEditor = ref(false);
const showImagePreview = ref(false);
const showCreateDialog = ref(false);
const showRenameDialog = ref(false);
const showBatchUploader = ref(false);
const showCompressor = ref(false);
const showDecompressor = ref(false);
const showPermissionDialog = ref(false);
const showCreateFolderDialog = ref(false);

// 临时数据
const newFolderName = ref('');
const editorFileName = ref('');
const editorContent = ref('');
const isSaving = ref(false);
const previewFileName = ref('');
const previewUrl = ref('');
const newFileName = ref('');
const renameNewName = ref('');
const renameTargetObj = ref<{ name: string; fullPath: string } | null>(null);
const compressTargets = ref<string[]>([]);
const decompressTargetFile = ref('');
const permissionTargets = ref<Array<{ name: string; fullPath: string; mode: string }>>([]);

// 监听窗口大小变化
const handleResize = () => {
  screenWidth.value = window.innerWidth;
};

const isImage = (name: string) => {
  const ext = name.split('.').pop()?.toLowerCase();
  return ['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp', 'bmp', 'svg'].includes(ext || '');
};

const isArchive = (name: string) => {
  const ext = name.split('.').pop()?.toLowerCase();
  return ['zip', 'jar'].includes(ext || '');
};

const getFileIcon = (row: FilesListModel) => {
  if (row.type === 'folder') {
    const name = row.name.toLowerCase();
    if (name === 'config' || name === 'settings') return { icon: SettingIcon, color: 'var(--td-warning-color)' };
    if (name.startsWith('world') || name === 'level') return { icon: EarthIcon, color: 'var(--td-success-color)' };
    if (['plugins', 'mods', 'libraries'].includes(name)) return { icon: AppIcon, color: 'var(--td-brand-color)' };
    if (['logs', 'crash-reports', 'cache', 'temp'].includes(name))
      return { icon: FolderIcon, color: 'var(--td-gray-color-6)' };
    return { icon: FolderIcon, color: 'var(--td-brand-color)' };
  }
  const ext = row.name.split('.').pop()?.toLowerCase();
  if (['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp'].includes(ext || ''))
    return { icon: FileImageIcon, color: 'var(--td-success-color)' };
  if (['jar', 'zip', 'rar', '7z', 'tar', 'gz'].includes(ext || '')) return { icon: FileZipIcon, color: '#722ed1' };
  if (['yml', 'yaml', 'json', 'properties', 'toml', 'xml', 'conf', 'sh', 'bat', 'cmd'].includes(ext || ''))
    return { icon: CodeIcon, color: 'var(--td-warning-color)' };
  if (['log', 'txt', 'md', 'lock'].includes(ext || '')) return { icon: FilePasteIcon, color: 'var(--td-gray-color-6)' };
  return { icon: FileIcon, color: 'var(--td-text-color-secondary)' };
};

const hasPermissionSupport = computed(() => {
  return fileList.value.some((item) => item.permission && item.permission !== '');
});

// 手机端只保留核心列
const columns = computed(() => {
  // 基础列配置
  const selectionCol = { colKey: 'row-select', type: 'multiple', width: isMobile.value ? 34 : 40 };
  const nameCol = { colKey: 'name', title: '文件名', ellipsis: true, width: 'auto' };
  const operationCol = {
    colKey: 'operation',
    title: '操作',
    width: isMobile.value ? 50 : 80,
    align: 'center',
    fixed: isMobile.value ? 'right' : undefined, // 手机端固定操作列在右侧
  };

  if (isMobile.value) {
    // 手机模式：只返回 勾选、文件名、操作
    return [selectionCol, nameCol, operationCol];
  }

  // PC 模式：返回完整列
  return [
    selectionCol,
    nameCol,
    { colKey: 'size', title: '大小', width: 100, align: 'right' },
    ...(hasPermissionSupport.value ? [{ colKey: 'permission', title: '权限', width: 80, align: 'center' }] : []),
    { colKey: 'lastModified', title: '修改时间', width: 180, align: 'center' },
    operationCol,
  ];
});

const breadcrumbs = computed(() => {
  const parts = currentPath.value.split('/').filter((p) => p);
  const crumbs = [{ name: '根目录', path: '' }];
  let accumPath = '';
  parts.forEach((part) => {
    accumPath = accumPath ? `${accumPath}/${part}` : part;
    crumbs.push({ name: part, path: accumPath });
  });
  // 手机端如果路径太长，只显示最后两级和根目录
  return crumbs;
});

const hasSelection = computed(() => selectedRowKeys.value.length > 0);

const formatSize = (size: number) => {
  if (size === 0) return '-';
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

const fetchData = async () => {
  loading.value = true;
  selectedRowKeys.value = [];
  try {
    const res = await getInstanceFilesList(instanceId.value, currentPath.value);
    fileList.value = res || [];
  } catch (error) {
    console.error(error);
  } finally {
    loading.value = false;
  }
};

// ---------------- 业务逻辑 ----------------
const openPreview = async (fileName: string) => {
  if (previewUrl.value) {
    window.URL.revokeObjectURL(previewUrl.value);
    previewUrl.value = '';
  }
  const fullPath = currentPath.value ? `${currentPath.value}/${fileName}` : fileName;
  const msg = MessagePlugin.loading('正在加载图片...');
  try {
    const blobData = await downloadFileStream(instanceId.value, fullPath);
    if (!(blobData instanceof Blob)) throw new Error('无效数据');
    previewUrl.value = window.URL.createObjectURL(blobData);
    previewFileName.value = fileName;
    showImagePreview.value = true;
    MessagePlugin.close(msg);
  } catch {
    MessagePlugin.close(msg);
    MessagePlugin.error('加载失败');
  }
};

const openEditor = async (fileName: string, isNewFile = false) => {
  if (isNewFile) {
    editorFileName.value = fileName;
    editorContent.value = '';
    showEditor.value = true;
    return;
  }
  if (isImage(fileName)) {
    openPreview(fileName);
    return;
  }
  const fullPath = currentPath.value ? `${currentPath.value}/${fileName}` : fileName;
  const msg = MessagePlugin.loading('正在读取文件...');
  try {
    const content = await getFileContent(instanceId.value, fullPath);
    editorFileName.value = fileName;
    editorContent.value = content;
    showEditor.value = true;
    MessagePlugin.close(msg);
  } catch (err: any) {
    MessagePlugin.close(msg);
    MessagePlugin.error('读取失败: ' + err.message);
  }
};

const handleOpenCreateDialog = () => {
  newFileName.value = '';
  showCreateDialog.value = true;
};
const handleConfirmCreate = () => {
  if (!newFileName.value.trim()) {
    MessagePlugin.warning('请输入文件名');
    return;
  }
  showCreateDialog.value = false;
  openEditor(newFileName.value, true);
};
const handleSaveFile = async (newContent: string) => {
  isSaving.value = true;
  try {
    const fullPath = currentPath.value ? `${currentPath.value}/${editorFileName.value}` : editorFileName.value;
    await saveFileContent(instanceId.value, fullPath, newContent);
    MessagePlugin.success('保存成功');
    showEditor.value = false;
    handleRefresh();
  } catch {
    MessagePlugin.error('保存失败');
  } finally {
    isSaving.value = false;
  }
};

const handleOpenCreateFolder = () => {
  newFolderName.value = '';
  showCreateFolderDialog.value = true;
};

const handleConfirmCreateFolder = async () => {
  if (!newFolderName.value.trim()) {
    MessagePlugin.warning('请输入文件夹名称');
    return;
  }
  try {
    await createDirectory(instanceId.value, currentPath.value, newFolderName.value);
    MessagePlugin.success('文件夹创建成功');
    showCreateFolderDialog.value = false;
    handleRefresh();
  } catch (error: any) {
    MessagePlugin.error(`创建失败: ${error.message || '未知错误'}`);
  }
};

const handleOpenRename = (row: any) => {
  renameTargetObj.value = {
    name: row.name,
    fullPath: currentPath.value ? `${currentPath.value}/${row.name}` : row.name,
  };
  renameNewName.value = row.name;
  showRenameDialog.value = true;
};
const handleConfirmRename = async () => {
  if (!renameNewName.value || !renameTargetObj.value) return;
  const newPath = currentPath.value ? `${currentPath.value}/${renameNewName.value}` : renameNewName.value;
  try {
    await renameFile(instanceId.value, renameTargetObj.value.fullPath, newPath);
    MessagePlugin.success('重命名成功');
    showRenameDialog.value = false;
    handleRefresh();
  } catch {
    MessagePlugin.error('重命名失败');
  }
};

const handleDelete = (row?: any) => {
  let targets: string[] = [];
  if (row) {
    targets = [row.name];
  } else {
    targets = [...selectedRowKeys.value];
  }
  if (targets.length === 0) return;
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除',
    body: `确定要永久删除这 ${targets.length} 项吗？`,
    theme: 'danger',
    onConfirm: async () => {
      try {
        const fullPaths = targets.map((name) => (currentPath.value ? `${currentPath.value}/${name}` : name));
        await deleteFiles(instanceId.value, fullPaths);
        MessagePlugin.success('删除成功');
        selectedRowKeys.value = [];
        handleRefresh();
        confirmDialog.hide();
      } catch {
        MessagePlugin.error('删除失败');
      }
    },
  });
};

const handleRowClick = (row: any) => {
  if (row.type === 'folder') {
    const separator = currentPath.value === '' ? '' : '/';
    currentPath.value = `${currentPath.value}${separator}${row.name}`;
  } else if (isImage(row.name)) {
    openPreview(row.name);
  } else {
    openEditor(row.name);
  }
};
const navigateTo = (path: string) => {
  currentPath.value = path;
};
const handleRefresh = () => fetchData();

const handleDownload = async (row?: any) => {
  let targets: string[] = [];
  if (row) {
    targets = [row.name];
  } else {
    targets = [...selectedRowKeys.value];
  }

  if (targets.length === 0) return;

  const { baseUrl, token } = userStore;
  const apiBase = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;

  for (const name of targets) {
    if (fileList.value.find((f) => f.name === name)?.type === 'folder') {
      MessagePlugin.warning(`暂不支持下载文件夹: ${name} 请压缩后再下载！`);
      continue;
    }

    const fullPath = currentPath.value ? `${currentPath.value}/${name}` : name;

    try {
      const downloadUrl = new URL(
        `${apiBase || window.location.origin}/api/files/instance/${instanceId.value}/download`,
      );

      downloadUrl.searchParams.append('path', fullPath);
      downloadUrl.searchParams.append('x-user-token', token); // 暂时先用token鉴权

      const link = document.createElement('a');
      link.href = downloadUrl.toString();
      link.style.display = 'none';
      link.download = name;

      document.body.appendChild(link);
      link.click();

      document.body.removeChild(link);
    } catch (e) {
      console.error(e);
      MessagePlugin.error(`创建下载链接失败: ${name}`);
    }
  }

  // 下载动作触发后，清空选中状态
  if (!row) selectedRowKeys.value = [];
};

const handleCompress = () => {
  if (selectedRowKeys.value.length === 0) return;
  compressTargets.value = [...selectedRowKeys.value];
  showCompressor.value = true;
};
const handleCompressSuccess = () => {
  selectedRowKeys.value = [];
  handleRefresh();
};

const handleOpenDecompress = (row: any) => {
  decompressTargetFile.value = row.name;
  showDecompressor.value = true;
};
const handleDecompressSuccess = () => {
  handleRefresh();
};

const handleOpenPermission = (row?: any) => {
  permissionTargets.value = [];
  if (row) {
    permissionTargets.value.push({
      name: row.name,
      fullPath: currentPath.value ? `${currentPath.value}/${row.name}` : row.name,
      mode: row.permission || '755',
    });
  } else {
    if (selectedRowKeys.value.length === 0) return;
    selectedRowKeys.value.forEach((key) => {
      const item = fileList.value.find((f) => f.name === key);
      if (item) {
        permissionTargets.value.push({
          name: item.name,
          fullPath: currentPath.value ? `${currentPath.value}/${item.name}` : item.name,
          mode: item.permission || '755',
        });
      }
    });
  }
  showPermissionDialog.value = true;
};
const handlePermissionSuccess = () => {
  selectedRowKeys.value = [];
  handleRefresh();
};

const handleUploadSuccess = () => handleRefresh();

// ———— 剪贴板 ——————
const clipboard = ref<string[]>([]); // 存储被复制/剪切的文件名(相对路径)
const clipboardMode = ref<'copy' | 'move'>('copy'); // 操作模式
const clipboardSourcePath = ref(''); // 记录来源路径，用于判断是否在同一目录

// 当前是否可以粘贴
const hasClipboard = computed(() => clipboard.value.length > 0);

// 粘贴按钮是否禁用
const isPasteDisabled = computed(() => {
  return currentPath.value === clipboardSourcePath.value;
});

// 处理复制
const handleCopy = () => {
  if (selectedRowKeys.value.length === 0) return;
  const paths = selectedRowKeys.value.map((name) => (currentPath.value ? `${currentPath.value}/${name}` : name));

  clipboard.value = paths;
  clipboardMode.value = 'copy';
  clipboardSourcePath.value = currentPath.value;

  selectedRowKeys.value = [];
  MessagePlugin.info(`已复制 ${paths.length} 项，请前往目标目录粘贴`);
};

// 处理剪切
const handleCut = () => {
  if (selectedRowKeys.value.length === 0) return;

  const paths = selectedRowKeys.value.map((name) => (currentPath.value ? `${currentPath.value}/${name}` : name));

  clipboard.value = paths;
  clipboardMode.value = 'move';
  clipboardSourcePath.value = currentPath.value;

  selectedRowKeys.value = [];
  MessagePlugin.info(`已剪切 ${paths.length} 项，请前往目标目录粘贴`);
};

// 取消粘贴
const handleCancelPaste = () => {
  clipboard.value = [];
  clipboardSourcePath.value = '';
  MessagePlugin.info('已取消操作');
};

// 执行粘贴
const handlePaste = async () => {
  if (clipboard.value.length === 0) return;

  const loadingMsg = MessagePlugin.loading('正在粘贴中...');
  try {
    if (clipboardMode.value === 'copy') {
      await copyFiles(instanceId.value, clipboard.value, currentPath.value);
    } else {
      await moveFiles(instanceId.value, clipboard.value, currentPath.value);
    }

    MessagePlugin.success('粘贴成功');
    clipboard.value = []; // 粘贴成功后清空剪贴板
    handleRefresh(); // 刷新当前列表
  } catch (error: any) {
    MessagePlugin.error(`粘贴失败: ${error.message || '未知错误'}`);
  } finally {
    MessagePlugin.close(loadingMsg);
  }
};

// 搜索和排序
// --- 状态变量 ---
const searchKey = ref('');
const sortType = ref('name'); // 默认按名称排序

const sortOptions = [
  { label: '名称 (A-Z)', value: 'name' },
  { label: '时间 (最新)', value: 'time' },
  { label: '大小 (从大到小)', value: 'size' },
];

// --- 计算属性 ---
const filteredFileList = computed(() => {
  let list = [...fileList.value]; // 浅拷贝

  // 搜索过滤
  if (searchKey.value) {
    const key = searchKey.value.toLowerCase();
    list = list.filter((item) => item.name.toLowerCase().includes(key));
  }

  // 排序逻辑
  list.sort((a, b) => {
    // 文件夹置顶优先级最高
    if (a.type === 'folder' && b.type !== 'folder') return -1;
    if (a.type !== 'folder' && b.type === 'folder') return 1;

    // 具体排序规则
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

// —————— 生命周期 ——————

watch(currentPath, (newPath) => {
  router.replace({ query: { ...route.query, path: newPath || undefined } });
  fetchData();
});

watch(instanceId, () => {
  if (route.name !== 'InstanceFiles') {
    return;
  }
  currentPath.value = '';
  selectedRowKeys.value = [];
  fetchData();
});

onMounted(() => {
  const queryPath = route.query.path as string;
  window.addEventListener('resize', handleResize);
  if (queryPath) currentPath.value = queryPath;
  else fetchData();
});

onUnmounted(() => {
  window.removeEventListener('resize', handleResize);
});
</script>

<template>
  <div class="flex flex-col mx-auto w-full pb-8">

    <div class="card-enter-anim design-card bg-[var(--td-bg-color-container)] border-y md:border border-zinc-200/60 dark:border-zinc-700/60 md:rounded-xl shadow-sm flex flex-col min-h-[calc(100vh-100px)] md:min-h-[600px] -mx-4 md:mx-0 overflow-hidden">

      <div class="sticky top-0 z-10 p-3 md:px-5 md:py-4 !bg-inherit border-b border-zinc-200/60 dark:border-zinc-800 flex items-center justify-between gap-4 overflow-x-auto hide-scrollbar">

        <div class="flex-1 flex items-center min-w-max">
          <t-breadcrumb :max-item-width="isMobile ? '80px' : '150px'">
            <t-breadcrumb-item
              v-for="(crumb, index) in breadcrumbs"
              :key="index"
              class="cursor-pointer whitespace-nowrap transition-colors hover:text-[var(--color-primary)]"
              @click="navigateTo(crumb.path)"
            >
              <template v-if="index === 0" #icon><home-icon /></template>
              {{ crumb.name }}
            </t-breadcrumb-item>
          </t-breadcrumb>
        </div>

        <div class="flex items-center gap-2 shrink-0 min-w-max">
          <t-input v-model="searchKey" placeholder="搜索文件..." class="!rounded-lg shadow-sm" :style="{ width: isMobile ? '120px' : '200px' }">
            <template #prefix-icon><search-icon class="text-zinc-400" /></template>
          </t-input>

          <t-select v-model="sortType" :options="sortOptions" class="!rounded-lg shadow-sm" :style="{ width: isMobile ? '110px' : '140px' }" placeholder="排序">
            <template #prefixIcon><filter-icon class="text-zinc-400" /></template>
          </t-select>

          <t-button variant="outline" size="medium" class="!rounded-lg !m-0" @click="changeUrl(`/instance/console/${instanceId}`)">
            <template #icon><rollback-icon /></template>
            <span v-if="!isMobile">控制台</span>
          </t-button>

          <t-dropdown>
            <t-button variant="outline" size="medium" class="!rounded-lg !m-0">
              <template #icon><file-add-icon /></template>
              <span v-if="!isMobile">新建</span>
            </t-button>
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item value="file" @click="handleOpenCreateDialog">
                  <file-icon class="mr-2" /> <span>新建文件</span>
                </t-dropdown-item>
                <t-dropdown-item value="folder" @click="handleOpenCreateFolder">
                  <folder-add-icon class="mr-2" /> <span>新建文件夹</span>
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
          </t-dropdown>

          <t-button theme="primary" size="medium" class="!rounded-lg shadow-sm !m-0" @click="showBatchUploader = true">
            <template #icon><cloud-upload-icon /></template>
            <span v-if="!isMobile">上传</span>
          </t-button>

          <t-button variant="outline" size="medium" class="!rounded-lg shrink-0 !m-0" @click="handleRefresh">
            <template #icon><refresh-icon /></template>
          </t-button>
        </div>
      </div>

      <div class="flex-1 w-full bg-transparent overflow-hidden [&_.t-table]:!border-t-0 [&_.t-table\_\_header]:!border-t-0 [&_.t-table\_\_header>tr>th]:!border-t-0">
        <t-table
          v-model:selected-row-keys="selectedRowKeys"
          :data="filteredFileList"
          :columns="columns as any"
          :row-key="'name'"
          :loading="loading"
          :hover="true"
          size="medium"
          class="custom-table"
        >
          <template #name="{ row }">
            <div class="flex items-center py-1.5 cursor-pointer group" @click.stop="handleRowClick(row)">
              <component :is="getFileIcon(row).icon" class="text-xl mr-2 shrink-0 transition-transform group-hover:scale-110" :style="{ color: getFileIcon(row).color }" />
              <span class="font-medium text-zinc-800 dark:text-zinc-200 group-hover:text-[var(--color-primary)] transition-colors truncate max-w-[calc(100vw-140px)] md:max-w-full">
                {{ row.name }}
              </span>
            </div>
          </template>

          <template #size="{ row }">
            <span class="text-[13px] font-mono text-zinc-500 dark:text-zinc-400">{{ formatSize(row.size) }}</span>
          </template>

          <template #permission="{ row }">
            <t-tag v-if="row.permission" variant="light-outline" size="small" class="!font-mono !rounded !justify-center !text-center">{{ row.permission }}</t-tag>
          </template>

          <template #lastModified="{ row }">
            <span class="text-[13px] text-zinc-500 dark:text-zinc-400">{{ formatTime(row.lastModified) }}</span>
          </template>

          <template #operation="{ row }">
            <div class="op-actions" @click.stop>
              <t-dropdown :placement="isMobile ? 'bottom-right' : 'bottom'">
                <t-button variant="text" shape="square" size="medium" class="!rounded-md hover:!bg-zinc-100 dark:hover:!bg-zinc-800 transition-colors">
                  <more-icon />
                </t-button>
                <template #dropdown>
                  <t-dropdown-menu>
                    <t-dropdown-item v-if="isArchive(row.name) && row.type !== 'folder'" value="decompress" @click="handleOpenDecompress(row)">
                      <file-zip-icon class="mr-2" /> <span>解压</span>
                    </t-dropdown-item>

                    <t-dropdown-item v-if="!(row.type === 'folder' || isArchive(row.name))" value="edit" @click="isImage(row.name) ? openPreview(row.name) : openEditor(row.name)">
                      <image-icon v-if="isImage(row.name)" class="mr-2" />
                      <edit-icon v-else class="mr-2" />
                      <span>{{ isImage(row.name) ? '预览' : '编辑' }}</span>
                    </t-dropdown-item>

                    <t-dropdown-item v-if="hasPermissionSupport" value="permission" @click="handleOpenPermission(row)">
                      <lock-on-icon class="mr-2" /> <span>权限</span>
                    </t-dropdown-item>

                    <t-dropdown-item value="download" @click="handleDownload(row)">
                      <download-icon class="mr-2" /> <span>下载</span>
                    </t-dropdown-item>

                    <t-dropdown-item value="rename" @click="handleOpenRename(row)">
                      <edit-icon class="mr-2" /> <span>重命名</span>
                    </t-dropdown-item>

                    <t-dropdown-item value="delete" class="danger-item !text-red-500 hover:!bg-red-50 dark:hover:!bg-red-500/10 transition-colors" @click="handleDelete(row)">
                      <delete-icon class="mr-2" /> <span>删除</span>
                    </t-dropdown-item>
                  </t-dropdown-menu>
                </template>
              </t-dropdown>
            </div>
          </template>

          <template #empty>
            <div class="py-16 flex flex-col items-center justify-center text-zinc-400 dark:text-zinc-500">
              <file-icon size="40px" class="opacity-60 mb-3" />
              <span class="text-sm font-medium">暂无文件</span>
            </div>
          </template>
        </t-table>
      </div>
    </div>

    <transition name="slide-up">
      <div v-if="hasSelection" class="design-card fixed bottom-6 md:bottom-10 left-1/2 -translate-x-1/2 w-11/12 md:w-max min-w-[280px] bg-[var(--td-bg-color-container)] border border-zinc-200/60 dark:border-zinc-700/60 shadow-[0_8px_30px_rgba(0,0,0,0.12)] rounded-full px-4 py-2.5 flex justify-between items-center z-[500] gap-4">

        <div class="text-sm font-medium text-zinc-700 dark:text-zinc-300 shrink-0">
          <template v-if="!isMobile">已选 </template>
          <span class="text-[var(--color-primary)] font-bold text-base mx-1">{{ selectedRowKeys.length }}</span>
          <template v-if="!isMobile">项</template>
        </div>

        <div class="flex items-center gap-1 md:gap-1.5 overflow-x-auto hide-scrollbar">
          <t-button size="small" variant="text" theme="primary" class="!rounded-full hover:!bg-[var(--color-primary)]/10" @click="handleCopy()">
            <template #icon><file-copy-icon /></template><span v-if="!isMobile">复制</span>
          </t-button>

          <t-button size="small" variant="text" theme="primary" class="!rounded-full hover:!bg-[var(--color-primary)]/10" @click="handleCut()">
            <template #icon><swap-icon /></template><span v-if="!isMobile">剪切</span>
          </t-button>

          <t-button size="small" variant="text" theme="primary" class="!rounded-full hover:!bg-[var(--color-primary)]/10" @click="handleCompress()">
            <template #icon><file-zip-icon /></template><span v-if="!isMobile">压缩</span>
          </t-button>

          <t-button size="small" variant="text" theme="primary" class="!rounded-full hover:!bg-[var(--color-primary)]/10" @click="handleDownload()">
            <template #icon><download-icon /></template><span v-if="!isMobile">下载</span>
          </t-button>

          <t-button
            v-if="hasPermissionSupport"
            size="small"
            variant="text"
            theme="primary"
            class="!rounded-full hover:!bg-[var(--color-primary)]/10"
            @click="handleOpenPermission()"
          >
            <template #icon><lock-on-icon /></template><span v-if="!isMobile">权限</span>
          </t-button>

          <t-button size="small" variant="text" theme="danger" class="!rounded-full hover:!bg-red-500/10" @click="handleDelete()">
            <template #icon><delete-icon /></template><span v-if="!isMobile">删除</span>
          </t-button>

          <div class="w-[1px] h-4 bg-zinc-200 dark:bg-zinc-700 mx-1 shrink-0"></div>
          <t-button size="small" variant="text" class="!rounded-full !text-zinc-500 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 shrink-0" @click="selectedRowKeys = []">取消</t-button>
        </div>
      </div>

      <div v-else-if="hasClipboard" class="design-card fixed bottom-6 md:bottom-10 left-1/2 -translate-x-1/2 w-11/12 md:w-max min-w-[280px] bg-[var(--td-bg-color-container)] border-2 border-[var(--color-primary)] shadow-[0_8px_30px_rgba(0,0,0,0.12)] shadow-[var(--color-primary)]/20 rounded-full px-5 py-3 flex justify-between items-center z-[501] gap-4">

        <div class="text-sm font-medium text-zinc-700 dark:text-zinc-300 shrink-0">
          <span v-if="clipboardMode === 'copy'">准备复制</span>
          <span v-else>准备移动</span>
          <span class="text-[var(--color-primary)] font-bold text-base mx-1">{{ clipboard.length }}</span> 项
        </div>

        <div class="flex items-center gap-2">
          <t-button theme="primary" :disabled="isPasteDisabled" class="!rounded-full shadow-sm" @click="handlePaste">
            <template #icon><file-paste-icon /></template>
            粘贴在此处
          </t-button>

          <t-button variant="text" theme="default" class="!rounded-full hover:!bg-zinc-200 dark:hover:!bg-zinc-700" @click="handleCancelPaste">
            <template #icon><close-icon /></template>
            取消
          </t-button>
        </div>
      </div>
    </transition>

    <file-editor v-model:visible="showEditor" :file-name="editorFileName" :content="editorContent" :loading="isSaving" @save="handleSaveFile" />
    <t-dialog v-model:visible="showCreateDialog" header="新建文件" :on-confirm="handleConfirmCreate">
      <t-input v-model="newFileName" placeholder="输入文件名" :autofocus="true" @enter="handleConfirmCreate" />
    </t-dialog>
    <t-dialog v-model:visible="showRenameDialog" header="重命名" :on-confirm="handleConfirmRename">
      <t-input v-model="renameNewName" placeholder="输入新名称" :autofocus="true" @enter="handleConfirmRename" />
    </t-dialog>
    <file-uploader v-model:visible="showBatchUploader" :instance-id="instanceId" :current-path="currentPath" @success="handleUploadSuccess" />
    <image-preview v-model:visible="showImagePreview" :file-name="previewFileName" :image-blob-url="previewUrl" />
    <file-compressor v-model:visible="showCompressor" :instance-id="instanceId" :current-path="currentPath" :files="compressTargets" @success="handleCompressSuccess" />
    <file-decompress v-model:visible="showDecompressor" :instance-id="instanceId" :current-path="currentPath" :file-name="decompressTargetFile" @success="handleDecompressSuccess" />
    <file-permission v-model:visible="showPermissionDialog" :instance-id="instanceId" :current-path="currentPath" :targets="permissionTargets" @success="handlePermissionSuccess" />
    <t-dialog v-model:visible="showCreateFolderDialog" header="新建文件夹" :on-confirm="handleConfirmCreateFolder">
      <t-input v-model="newFolderName" placeholder="输入文件夹名称" :autofocus="true" @enter="handleConfirmCreateFolder" />
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

.hide-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
  &::-webkit-scrollbar {
    display: none;
  }
}

/* === 面包屑 Light 样式污染修复 === */
:deep(.t-breadcrumb__item.light) {
  background-color: transparent !important;
  color: unset !important;
}

/* ================= 卡片整体进场动画 ================= */
.card-enter-anim {
  animation: slideUpFade 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUpFade {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* ================= 悬浮条进出动画 ================= */
.slide-up-enter-active,
.slide-up-leave-active {
  transition: transform 0.3s cubic-bezier(0.34, 1.56, 0.64, 1), opacity 0.3s ease;
}
.slide-up-enter-from,
.slide-up-leave-to {
  transform: translate(-50%, 100%);
  opacity: 0;
}

/* ================= 文件列雅瀑布流进场 ================= */
@keyframes tableRowSlideUp {
  from {
    opacity: 0;
    transform: translateY(12px) translateZ(0);
  }
  to {
    opacity: 1;
    transform: translateY(0) translateZ(0);
  }
}

:deep(.t-table tbody tr) {
  animation: tableRowSlideUp 0.35s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

/* 首屏的 15 行应用错落延迟 */
.generate-row-delays(@n, @i: 1) when (@i =< @n) {
  :deep(.t-table tbody tr:nth-child(@{i})) {
    animation-delay: (@i * 0.025s);
  }
  .generate-row-delays(@n, (@i + 1));
}
.generate-row-delays(15);

:deep(.t-table tbody tr:nth-child(n+16)) {
  animation-delay: 0.35s;
}
</style>
