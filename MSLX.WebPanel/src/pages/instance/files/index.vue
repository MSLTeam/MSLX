<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  HomeIcon, RefreshIcon,
  CloudUploadIcon, DeleteIcon, DownloadIcon, MoreIcon
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { getInstanceFilesList } from '@/api/files';
import type { FilesListModel } from '@/api/model/files';

const route = useRoute();
const router = useRouter();
const instanceId = Number(route.params.id);

const loading = ref(false);
const fileList = ref<FilesListModel[]>([]);
const currentPath = ref('');
const selectedRowKeys = ref<string[]>([]);

// 表格列定义
const columns = [
  { colKey: 'row-select', type: 'multiple', width: 40 },
  { colKey: 'name', title: '文件名', ellipsis: true, width: 'auto' }, // 这里的 width auto 让它自适应
  { colKey: 'size', title: '大小', width: 100, align: 'right' },
  { colKey: 'lastModified', title: '修改时间', width: 180, align: 'center' },
  { colKey: 'operation', title: '操作', width: 80, align: 'center' }
];

const breadcrumbs = computed(() => {
  const parts = currentPath.value.split('/').filter(p => p);
  const crumbs = [{ name: '根目录', path: '' }];
  let accumPath = '';
  parts.forEach(part => {
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
  } catch (error) {
    console.error(error);
  } finally {
    loading.value = false;
  }
};

const handleRowClick = (row: any) => {
  if (row.type === 'folder') {
    const separator = currentPath.value === '' ? '' : '/';
    currentPath.value = `${currentPath.value}${separator}${row.name}`;
  }
};

const navigateTo = (path: string) => { currentPath.value = path; };
const handleRefresh = () => fetchData();
const handleUpload = () => MessagePlugin.info('点击上传');
const handleDownload = (row?: any) => MessagePlugin.success('下载' + (row ? row.name : '选中项'));
const handleDelete = (row?: any) => MessagePlugin.warning('删除' + (row ? row.name : '选中项'));

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
  <div class="file-manager-container">

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
        <t-button theme="primary" size="medium" @click="handleUpload">
          <template #icon><cloud-upload-icon /></template>
          <span class="btn-text">上传</span>
        </t-button>
        <t-button variant="outline" size="medium" @click="handleRefresh">
          <template #icon><refresh-icon /></template>
        </t-button>
      </div>
    </div>

    <div class="table-wrapper">
      <t-card :bordered="false" class="table-card">
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
              <t-icon
                class="file-icon"
                :name="row.type === 'folder' ? 'folder' : 'file'"
                :style="{ color: row.type === 'folder' ? 'var(--td-brand-color)' : 'var(--td-text-color-secondary)' }"
              />
              <span class="name-text">{{ row.name }}</span>
            </div>
          </template>
          <template #size="{ row }">{{ formatSize(row.size) }}</template>
          <template #lastModified="{ row }">{{ formatTime(row.lastModified) }}</template>
          <template #operation="{ row }">
            <div class="op-actions">
              <t-dropdown :options="[
                  { content: '下载', value: 'download', onClick: () => handleDownload(row) },
                  { content: '重命名', value: 'rename' },
                  { content: '删除', value: 'delete', theme: 'error', onClick: () => handleDelete(row) }
                ]">
                <t-button variant="text" shape="square"><more-icon /></t-button>
              </t-dropdown>
            </div>
          </template>
          <template #empty><div class="empty-state">暂无文件</div></template>
        </t-table>
      </t-card>
    </div>

    <transition name="slide-up">
      <div v-if="hasSelection" class="selection-bar">
        <div class="selection-info">已选 <span>{{ selectedRowKeys.length }}</span> 项</div>
        <div class="selection-actions">
          <t-button size="small" variant="text" theme="primary" @click="handleDownload()"><template #icon><download-icon /></template></t-button>
          <t-button size="small" variant="text" theme="danger" @click="handleDelete()"><template #icon><delete-icon /></template></t-button>
          <t-button size="small" variant="text" @click="selectedRowKeys = []">取消</t-button>
        </div>
      </div>
    </transition>
  </div>
</template>

<style scoped lang="less">
/* --- 布局容器 --- */
.file-manager-container {
  /* 【改动】去掉 height: 100% 和 overflow: hidden */
  /* 让容器随内容自然生长，使用浏览器原生滚动条 */
  min-height: 100%;
  background-color: var(--td-bg-color-container);
  border-radius: var(--td-radius-large);
  position: relative;
}

/* --- 顶部工具栏 (吸顶核心) --- */
.toolbar {
  /* 【核心】Sticky 吸顶 */
  position: sticky;
  top: 0;
  z-index: 100; /* 保证在表格上面 */

  padding: 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--td-component-stroke);

  /* 【关键】必须给背景色，否则透明的话表格内容会从下面透出来 */
  background: var(--td-bg-color-container);

  flex-wrap: nowrap;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  gap: 16px;

  &::-webkit-scrollbar { display: none; }
  scrollbar-width: none;

  .breadcrumb-area {
    flex: 1;
    display: flex;
    align-items: center;
    min-width: max-content;
    .crumb-item {
      cursor: pointer;
      white-space: nowrap;
      transition: color 0.2s;
      &:hover { color: var(--td-brand-color); }
    }
  }

  .actions-area {
    display: flex;
    gap: 12px;
    flex-shrink: 0;
    min-width: max-content;
  }
}

@media (max-width: 768px) {
  .toolbar { padding: 12px; }
  .actions-area .btn-text { display: none; }
}

/* --- 表格区域 --- */
.table-wrapper {
  /* 去掉 overflow: auto，让表格撑开页面 */
  width: 100%;
}

.table-card {
  border-radius: 0;
  :deep(.t-card__body) { padding: 0; }
}

.file-table {
  /* 让 TDesign 表格自适应 */
  .file-name-cell {
    display: flex; align-items: center; padding: 4px 0;
    .file-icon { font-size: 20px; margin-right: 8px; flex-shrink: 0; }
    .name-text { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  }
}

/* --- 底部浮动栏 (胶囊悬浮) --- */
.selection-bar {
  /* 【核心】Fixed 固定定位 */
  position: fixed; /* 改为 fixed，相对于屏幕定位 */
  bottom: 40px;    /* 距离屏幕底部 40px */
  left: 50%;
  transform: translateX(-50%);

  /* 胶囊样式 */
  width: max-content;
  min-width: 280px;
  max-width: 90%;
  background: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke);
  box-shadow: var(--td-shadow-3);
  border-radius: 48px;
  padding: 8px 24px;

  display: flex; justify-content: space-between; align-items: center;
  z-index: 500; /* 层级极高，保证在最上层 */
  gap: 24px;

  .selection-info span { color: var(--td-brand-color); font-weight: bold; margin: 0 4px; font-size: 16px; }
  .selection-actions { display: flex; gap: 12px; }
}

.slide-up-enter-active, .slide-up-leave-active { transition: transform 0.3s ease, opacity 0.3s ease; }
.slide-up-enter-from, .slide-up-leave-to { transform: translateY(100%); opacity: 0; }
</style>
