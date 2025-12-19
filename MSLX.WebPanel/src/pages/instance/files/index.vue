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
import {useUserStore} from "@/store";

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
      const downloadUrl = new URL(`${apiBase || window.location.origin }/api/files/instance/${instanceId.value}/download`);

      downloadUrl.searchParams.append('path', fullPath);
      downloadUrl.searchParams.append('x-user-token', token); // 关键：鉴权 Token

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
  <div class="file-manager-wrapper">
    <t-card :bordered="false" class="file-manager-card">
      <div class="toolbar">
        <div class="breadcrumb-area">
          <t-breadcrumb :max-item-width="isMobile ? '80px' : '150px'">
            <t-breadcrumb-item
              v-for="(crumb, index) in breadcrumbs"
              :key="index"
              class="crumb-item"
              @click="navigateTo(crumb.path)"
            >
              <template v-if="index === 0" #icon><home-icon /></template>
              {{ crumb.name }}
            </t-breadcrumb-item>
          </t-breadcrumb>
        </div>
        <div class="actions-area">
          <t-button variant="outline" size="medium" @click="changeUrl(`/instance/console/${instanceId}`)">
            <template #icon><rollback-icon /></template>
            <span v-if="!isMobile">控制台</span>
          </t-button>
          <t-dropdown
            :options="[
              { content: '新建文件', value: 'file', onClick: handleOpenCreateDialog },
              { content: '新建文件夹', value: 'folder', onClick: handleOpenCreateFolder },
            ]"
          >
            <t-button variant="outline" size="medium">
              <template #icon><file-add-icon /></template>
              <span v-if="!isMobile">新建</span>
            </t-button>
          </t-dropdown>
          <t-button theme="primary" size="medium" @click="showBatchUploader = true">
            <template #icon><cloud-upload-icon /></template>
            <span v-if="!isMobile">上传</span>
          </t-button>
          <t-button variant="outline" size="medium" @click="handleRefresh">
            <template #icon><refresh-icon /></template>
          </t-button>
        </div>
      </div>

      <div class="table-wrapper">
        <t-table
          v-model:selected-row-keys="selectedRowKeys"
          :data="fileList"
          :columns="columns as any"
          :row-key="'name'"
          :loading="loading"
          :hover="true"
          size="medium"
          class="file-table"
        >
          <template #name="{ row }">
            <div class="file-name-cell" @click.stop="handleRowClick(row)">
              <component :is="getFileIcon(row).icon" class="file-icon" :style="{ color: getFileIcon(row).color }" />
              <span class="name-text">{{ row.name }}</span>
            </div>
          </template>
          <template #size="{ row }">{{ formatSize(row.size) }}</template>
          <template #permission="{ row }">
            <t-tag v-if="row.permission" variant="light" size="small">{{ row.permission }}</t-tag>
          </template>
          <template #lastModified="{ row }">{{ formatTime(row.lastModified) }}</template>
          <template #operation="{ row }">
            <div class="op-actions">
              <t-dropdown
                :placement="isMobile ? 'bottom-right' : 'bottom'"
                :options="
                  [
                    {
                      content: '解压',
                      value: 'decompress',
                      onClick: () => handleOpenDecompress(row),
                      show: isArchive(row.name) && row.type !== 'folder',
                    },
                    {
                      content: isImage(row.name) ? '预览' : '编辑',
                      value: 'edit',
                      onClick: () => (isImage(row.name) ? openPreview(row.name) : openEditor(row.name)),
                      disabled: row.type === 'folder' || isArchive(row.name),
                    },
                    {
                      content: '权限',
                      value: 'permission',
                      onClick: () => handleOpenPermission(row),
                      show: hasPermissionSupport,
                    },
                    { content: '下载', value: 'download', onClick: () => handleDownload(row) },
                    { content: '重命名', value: 'rename', onClick: () => handleOpenRename(row) },
                    { content: '删除', value: 'delete', theme: 'error', onClick: () => handleDelete(row) },
                  ].filter((opt: any) => opt.show !== false) as any
                "
              >
                <t-button variant="text" shape="square" size="medium"><more-icon /></t-button>
              </t-dropdown>
            </div>
          </template>
          <template #empty><div class="empty-state">暂无文件</div></template>
        </t-table>
      </div>
    </t-card>

    <transition name="slide-up">
      <div v-if="hasSelection" class="selection-bar">
        <div v-if="!isMobile" class="selection-info">
          已选 <span>{{ selectedRowKeys.length }}</span> 项
        </div>
        <div v-else class="selection-info">
          <span>{{ selectedRowKeys.length }}</span>
        </div>

        <div class="selection-actions">
          <t-button size="small" variant="text" theme="primary" @click="handleCopy()">
            <template #icon><file-copy-icon /></template><span v-if="!isMobile">复制</span>
          </t-button>

          <t-button size="small" variant="text" theme="primary" @click="handleCut()">
            <template #icon><swap-icon /></template><span v-if="!isMobile">剪切</span>
          </t-button>

          <t-button size="small" variant="text" theme="primary" @click="handleCompress()">
            <template #icon><file-zip-icon /></template><span v-if="!isMobile">压缩</span>
          </t-button>
          <t-button size="small" variant="text" theme="primary" @click="handleDownload()">
            <template #icon><download-icon /></template><span v-if="!isMobile">下载</span>
          </t-button>
          <t-button
            v-if="hasPermissionSupport"
            size="small"
            variant="text"
            theme="primary"
            @click="handleOpenPermission()"
          >
            <template #icon><lock-on-icon /></template><span v-if="!isMobile">权限</span>
          </t-button>
          <t-button size="small" variant="text" theme="danger" @click="handleDelete()">
            <template #icon><delete-icon /></template><span v-if="!isMobile">删除</span>
          </t-button>
          <t-button size="small" variant="text" @click="selectedRowKeys = []">取消</t-button>
        </div>
      </div>

      <div v-else-if="hasClipboard" class="selection-bar clipboard-bar">
        <div class="selection-info">
          <span v-if="clipboardMode === 'copy'">准备复制</span>
          <span v-else>准备移动</span>
          <span>{{ clipboard.length }}</span> 项
        </div>

        <div class="selection-actions">
          <t-button
            theme="primary"
            :disabled="isPasteDisabled"
            @click="handlePaste"
          >
            <template #icon><file-paste-icon /></template>
            粘贴在此处
          </t-button>

          <t-button variant="text" theme="default" @click="handleCancelPaste">
            <template #icon><close-icon /></template>
            取消
          </t-button>
        </div>
      </div>
    </transition>

    <file-editor
      v-model:visible="showEditor"
      :file-name="editorFileName"
      :content="editorContent"
      :loading="isSaving"
      @save="handleSaveFile"
    />
    <t-dialog v-model:visible="showCreateDialog" header="新建文件" :on-confirm="handleConfirmCreate">
      <t-input v-model="newFileName" placeholder="输入文件名" :autofocus="true" @enter="handleConfirmCreate" />
    </t-dialog>
    <t-dialog v-model:visible="showRenameDialog" header="重命名" :on-confirm="handleConfirmRename">
      <t-input v-model="renameNewName" placeholder="输入新名称" :autofocus="true" @enter="handleConfirmRename" />
    </t-dialog>
    <file-uploader
      v-model:visible="showBatchUploader"
      :instance-id="instanceId"
      :current-path="currentPath"
      @success="handleUploadSuccess"
    />
    <image-preview v-model:visible="showImagePreview" :file-name="previewFileName" :image-blob-url="previewUrl" />
    <file-compressor
      v-model:visible="showCompressor"
      :instance-id="instanceId"
      :current-path="currentPath"
      :files="compressTargets"
      @success="handleCompressSuccess"
    />
    <file-decompress
      v-model:visible="showDecompressor"
      :instance-id="instanceId"
      :current-path="currentPath"
      :file-name="decompressTargetFile"
      @success="handleDecompressSuccess"
    />
    <file-permission
      v-model:visible="showPermissionDialog"
      :instance-id="instanceId"
      :current-path="currentPath"
      :targets="permissionTargets"
      @success="handlePermissionSuccess"
    />
    <t-dialog v-model:visible="showCreateFolderDialog" header="新建文件夹" :on-confirm="handleConfirmCreateFolder">
      <t-input
        v-model="newFolderName"
        placeholder="输入文件夹名称"
        :autofocus="true"
        @enter="handleConfirmCreateFolder"
      />
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
.file-manager-wrapper {
  padding-bottom: 20px;
}
.file-manager-card {
  min-height: 600px;
  overflow: hidden;
  border-radius: var(--td-radius-large);
  :deep(.t-card__body) {
    padding: 0;
    display: flex;
    flex-direction: column;
  }
}
.toolbar {
  position: sticky;
  top: 0;
  z-index: 100;
  padding: 16px 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--td-component-stroke);
  background: var(--td-bg-color-container);
  flex-wrap: nowrap;
  overflow-x: auto;
  gap: 16px;
  /* 隐藏滚动条但保留功能 */
  &::-webkit-scrollbar {
    display: none;
  }
  scrollbar-width: none;

  .breadcrumb-area {
    flex: 1;
    display: flex;
    align-items: center;
    min-width: max-content;
  }
  .breadcrumb-area .crumb-item {
    cursor: pointer;
    white-space: nowrap;
    transition: color 0.2s;
  }
  .breadcrumb-area .crumb-item:hover {
    color: var(--td-brand-color);
  }
  .actions-area {
    display: flex;
    gap: 8px; /* 移动端缩小间距 */
    flex-shrink: 0;
    min-width: max-content;
    align-items: center;
  }
}

/* 移动端特定样式调整 */
@media (max-width: 768px) {
  .toolbar {
    padding: 10px 12px;
  }
  .file-manager-card {
    border-radius: 0; /* 手机端去掉圆角以利用空间 */
    min-height: calc(100vh - 100px);
  }
  /* 调整表格内边距 */
  :deep(.t-table th),
  :deep(.t-table td) {
    padding: 8px !important;
  }
}

.table-wrapper {
  width: 100%;
  flex: 1;
}
.file-table .file-name-cell {
  display: flex;
  align-items: center;
  padding: 4px 0;
  cursor: pointer;
}
.file-table .file-icon {
  font-size: 20px;
  margin-right: 8px;
  flex-shrink: 0;
}
.file-table .name-text {
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  /* 确保文件名在手机端能够自适应截断 */
  max-width: calc(100vw - 140px);
}
@media (min-width: 768px) {
  .file-table .name-text {
    max-width: 100%;
  }
}

.file-table .name-text:hover {
  color: var(--td-brand-color);
}
.selection-bar {
  position: fixed;
  bottom: 40px;
  left: 50%;
  transform: translateX(-50%);
  width: max-content;
  min-width: 280px;
  max-width: 95%;
  background: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke);
  box-shadow: var(--td-shadow-3);
  border-radius: 48px;
  padding: 8px 24px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  z-index: 500;
  gap: 16px;
  .selection-info span {
    color: var(--td-brand-color);
    font-weight: bold;
    margin: 0 4px;
    font-size: 16px;
  }
  .selection-actions {
    display: flex;
    gap: 4px; /* 移动端更紧凑 */
  }
}

@media (max-width: 768px) {
  .selection-bar {
    bottom: 20px;
    padding: 8px 16px;
    min-width: auto;
    width: 90%;
  }
}

.slide-up-enter-active,
.slide-up-leave-active {
  transition:
    transform 0.3s ease,
    opacity 0.3s ease;
}
.slide-up-enter-from,
.slide-up-leave-to {
  transform: translateY(100%);
  opacity: 0;
}
.clipboard-bar {
  z-index: 501;
  border-color: var(--td-brand-color);
  background-color: var(--td-bg-color-container);
}
</style>
