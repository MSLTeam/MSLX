<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  HomeIcon, RefreshIcon, CloudUploadIcon, DeleteIcon, DownloadIcon, MoreIcon, FileAddIcon,
  FolderIcon, FileIcon, SettingIcon, EarthIcon, AppIcon,
  FileImageIcon, FilePasteIcon, CodeIcon, FileZipIcon, ServiceIcon
} from 'tdesign-icons-vue-next';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  getInstanceFilesList,
  getFileContent,
  saveFileContent,
  renameFile,
  deleteFiles
} from '@/api/files';
import type { FilesListModel } from '@/api/model/files';
import FileEditor from './components/FileEditor.vue';
import FileUploader from './components/FileUploader.vue';

const route = useRoute();
const router = useRouter();
const instanceId = Number(route.params.id);

const loading = ref(false);
const fileList = ref<FilesListModel[]>([]);
const currentPath = ref('');
const selectedRowKeys = ref<string[]>([]);

// 编辑器状态
const showEditor = ref(false);
const editorFileName = ref('');
const editorContent = ref('');
const isSaving = ref(false);

// 弹窗状态
const showCreateDialog = ref(false);
const showRenameDialog = ref(false);
const showBatchUploader = ref(false); // 上传弹窗控制

const newFileName = ref('');
const renameNewName = ref('');
const renameTargetObj = ref<{ name: string; fullPath: string } | null>(null);

// 图标映射
const getFileIcon = (row: FilesListModel) => {
  if (row.type === 'folder') {
    const name = row.name.toLowerCase();
    if (name === 'config' || name === 'settings') return { icon: SettingIcon, color: 'var(--td-warning-color)' };
    if (name.startsWith('world') || name === 'level') return { icon: EarthIcon, color: 'var(--td-success-color)' };
    if (name === 'plugins' || name === 'mods' || name === 'libraries') return { icon: AppIcon, color: 'var(--td-brand-color)' };
    if (['logs', 'crash-reports', 'cache', 'temp'].includes(name)) return { icon: FolderIcon, color: 'var(--td-gray-color-6)' };
    return { icon: FolderIcon, color: 'var(--td-brand-color)' };
  }
  const ext = row.name.split('.').pop()?.toLowerCase();
  if (['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp'].includes(ext || '')) return { icon: FileImageIcon, color: 'var(--td-success-color)' };
  if (['jar', 'zip', 'rar', '7z', 'tar', 'gz'].includes(ext || '')) return { icon: FileZipIcon, color: '#722ed1' };
  if (['yml', 'yaml', 'json', 'properties', 'toml', 'xml', 'conf', 'sh', 'bat', 'cmd'].includes(ext || '')) return { icon: CodeIcon, color: 'var(--td-warning-color)' };
  if (['log', 'txt', 'md', 'lock'].includes(ext || '')) return { icon: FilePasteIcon, color: 'var(--td-gray-color-6)' };
  if (['db', 'db-wal', 'db-shm', 'dat'].includes(ext || '')) return { icon: ServiceIcon, color: 'var(--td-gray-color-8)' };
  return { icon: FileIcon, color: 'var(--td-text-color-secondary)' };
};

const columns = [
  { colKey: 'row-select', type: 'multiple', width: 40 },
  { colKey: 'name', title: '文件名', ellipsis: true, width: 'auto' },
  { colKey: 'size', title: '大小', width: 100, align: 'right' },
  { colKey: 'lastModified', title: '修改时间', width: 180, align: 'center' },
  { colKey: 'operation', title: '操作', width: 80, align: 'center' },
];

const breadcrumbs = computed(() => {
  const parts = currentPath.value.split('/').filter((p) => p);
  const crumbs = [{ name: '根目录', path: '' }];
  let accumPath = '';
  parts.forEach((part) => {
    accumPath = accumPath ? `${accumPath}/${part}` : part;
    crumbs.push({ name: part, path: accumPath });
  });
  return crumbs;
});

const hasSelection = computed(() => selectedRowKeys.value.length > 0);

const formatSize = (size: number) => {
  if (size === 0) return '-';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (size >= 1024 && i < units.length - 1) { size /= 1024; i++; }
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
    const res = await getInstanceFilesList(instanceId, currentPath.value);
    fileList.value = res || [];
  } catch (error) { console.error(error); }
  finally { loading.value = false; }
};

// --- 动作逻辑 ---

const openEditor = async (fileName: string, isNewFile = false) => {
  if (isNewFile) {
    editorFileName.value = fileName;
    editorContent.value = '';
    showEditor.value = true;
    return;
  }
  const fullPath = currentPath.value ? `${currentPath.value}/${fileName}` : fileName;
  const msg = MessagePlugin.loading('正在读取文件...');
  try {
    const content = await getFileContent(instanceId, fullPath);
    editorFileName.value = fileName;
    editorContent.value = content;
    showEditor.value = true;
    MessagePlugin.close(msg);
  } catch (err: any) {
    MessagePlugin.close(msg);
    MessagePlugin.error('无法读取文件内容: ' + err.message);
  }
};

const handleOpenCreateDialog = () => { newFileName.value = ''; showCreateDialog.value = true; };
const handleConfirmCreate = () => {
  if (!newFileName.value.trim()) { MessagePlugin.warning('请输入文件名'); return; }
  showCreateDialog.value = false; openEditor(newFileName.value, true);
};

const handleSaveFile = async (newContent: string) => {
  isSaving.value = true;
  try {
    const fullPath = currentPath.value ? `${currentPath.value}/${editorFileName.value}` : editorFileName.value;
    await saveFileContent(instanceId, fullPath, newContent);
    MessagePlugin.success('保存成功'); showEditor.value = false; handleRefresh();
  } catch (err: any) { MessagePlugin.error(err.message || '保存失败'); }
  finally { isSaving.value = false; }
};

const handleOpenRename = (row: any) => {
  renameTargetObj.value = { name: row.name, fullPath: currentPath.value ? `${currentPath.value}/${row.name}` : row.name };
  renameNewName.value = row.name; showRenameDialog.value = true;
};

const handleConfirmRename = async () => {
  if (!renameNewName.value || !renameTargetObj.value) return;
  const newPath = currentPath.value ? `${currentPath.value}/${renameNewName.value}` : renameNewName.value;
  try {
    await renameFile(instanceId, renameTargetObj.value.fullPath, newPath);
    MessagePlugin.success('重命名成功'); showRenameDialog.value = false; handleRefresh();
  } catch (err: any) { MessagePlugin.error(err.message || '重命名失败'); }
};

const handleDelete = (row?: any) => {
  let targets: string[] = [];
  if (row) { targets = [row.name]; } else { targets = [...selectedRowKeys.value]; }
  if (targets.length === 0) return;
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除',
    body: `确定要永久删除这 ${targets.length} 项吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      try {
        const fullPaths = targets.map(name => currentPath.value ? `${currentPath.value}/${name}` : name);
        await deleteFiles(instanceId, fullPaths);
        MessagePlugin.success('删除成功'); selectedRowKeys.value = []; handleRefresh(); confirmDialog.hide();
      } catch (err: any) { MessagePlugin.error(err.message || '删除失败'); }
    }
  });
};

const handleRowClick = (row: any) => {
  if (row.type === 'folder') {
    const separator = currentPath.value === '' ? '' : '/';
    currentPath.value = `${currentPath.value}${separator}${row.name}`;
  } else { openEditor(row.name); }
};

const navigateTo = (path: string) => { currentPath.value = path; };
const handleRefresh = () => fetchData();
const handleDownload = (row?: any) => MessagePlugin.success('下载 ' + row?.name);

const handleUploadSuccess = () => {
  handleRefresh();
};

watch(currentPath, (newPath) => {
  router.replace({ query: { ...route.query, path: newPath || undefined } });
  fetchData();
});

onMounted(() => {
  const queryPath = route.query.path as string;
  if (queryPath) currentPath.value = queryPath;
  else fetchData();
});
</script>

<template>
  <div class="file-manager-wrapper">

    <t-card :bordered="false" class="file-manager-card">
      <div class="toolbar">
        <div class="breadcrumb-area">
          <t-breadcrumb :max-item-width="'150px'">
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
          <t-button variant="outline" size="medium" @click="handleOpenCreateDialog">
            <template #icon><file-add-icon /></template>
            新建文件
          </t-button>

          <t-button theme="primary" size="medium" @click="showBatchUploader = true">
            <template #icon><cloud-upload-icon /></template>
            <span class="btn-text">上传文件</span>
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
              <component
                :is="getFileIcon(row).icon"
                class="file-icon"
                :style="{ color: getFileIcon(row).color }"
              />
              <span class="name-text">{{ row.name }}</span>
            </div>
          </template>
          <template #size="{ row }">{{ formatSize(row.size) }}</template>
          <template #lastModified="{ row }">{{ formatTime(row.lastModified) }}</template>
          <template #operation="{ row }">
            <div class="op-actions">
              <t-dropdown
                :options="[
                  { content: '编辑', value: 'edit', onClick: () => openEditor(row.name), disabled: row.type === 'folder' },
                  { content: '下载', value: 'download', onClick: () => handleDownload(row) },
                  { content: '重命名', value: 'rename', onClick: () => handleOpenRename(row) },
                  { content: '删除', value: 'delete', theme: 'error', onClick: () => handleDelete(row) },
                ]"
              >
                <t-button variant="text" shape="square"><more-icon /></t-button>
              </t-dropdown>
            </div>
          </template>
          <template #empty><div class="empty-state">暂无文件</div></template>
        </t-table>
      </div>
    </t-card>

    <transition name="slide-up">
      <div v-if="hasSelection" class="selection-bar">
        <div class="selection-info">
          已选 <span>{{ selectedRowKeys.length }}</span> 项
        </div>
        <div class="selection-actions">
          <t-button size="small" variant="text" theme="primary" @click="handleDownload()"
          ><template #icon><download-icon /></template
          ></t-button>
          <t-button size="small" variant="text" theme="danger" @click="handleDelete()"
          ><template #icon><delete-icon /></template
          ></t-button>
          <t-button size="small" variant="text" @click="selectedRowKeys = []">取消</t-button>
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

    <t-dialog
      v-model:visible="showCreateDialog"
      header="新建文件"
      :on-confirm="handleConfirmCreate"
    >
      <t-input
        v-model="newFileName"
        placeholder="请输入文件名（例如 config.yml）"
        :autofocus="true"
        @enter="handleConfirmCreate"
      />
    </t-dialog>

    <t-dialog
      v-model:visible="showRenameDialog"
      header="重命名"
      :on-confirm="handleConfirmRename"
    >
      <t-input
        v-model="renameNewName"
        placeholder="请输入新名称"
        :autofocus="true"
        @enter="handleConfirmRename"
      />
    </t-dialog>

    <file-uploader
      v-model:visible="showBatchUploader"
      :instance-id="instanceId"
      :current-path="currentPath"
      @success="handleUploadSuccess"
    />
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
  &::-webkit-scrollbar { display: none; }

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
  .breadcrumb-area .crumb-item:hover { color: var(--td-brand-color); }

  .actions-area {
    display: flex;
    gap: 12px;
    flex-shrink: 0;
    min-width: max-content;
    align-items: center;
  }
}

@media (max-width: 768px) {
  .toolbar { padding: 12px 16px; }
  .actions-area .btn-text { display: none; }
}

.table-wrapper { width: 100%; flex: 1; }

.file-table .file-name-cell {
  display: flex;
  align-items: center;
  padding: 4px 0;
  cursor: pointer;
}
.file-table .file-icon { font-size: 20px; margin-right: 8px; flex-shrink: 0; }
.file-table .name-text { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.file-table .name-text:hover { color: var(--td-brand-color); }

.selection-bar {
  position: fixed; bottom: 40px; left: 50%; transform: translateX(-50%);
  width: max-content; min-width: 280px; max-width: 90%;
  background: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke);
  box-shadow: var(--td-shadow-3);
  border-radius: 48px;
  padding: 8px 24px;
  display: flex; justify-content: space-between; align-items: center;
  z-index: 500; gap: 24px;

  .selection-info span { color: var(--td-brand-color); font-weight: bold; margin: 0 4px; font-size: 16px; }
  .selection-actions { display: flex; gap: 12px; }
}
.slide-up-enter-active, .slide-up-leave-active { transition: transform 0.3s ease, opacity 0.3s ease; }
.slide-up-enter-from, .slide-up-leave-to { transform: translateY(100%); opacity: 0; }
</style>
