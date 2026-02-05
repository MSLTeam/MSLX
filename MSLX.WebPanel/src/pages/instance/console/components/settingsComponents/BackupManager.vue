<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin, DialogPlugin, type PrimaryTableCol, type TableRowData } from 'tdesign-vue-next';
import { DeleteIcon, DownloadIcon, TimeIcon, FileIcon, RefreshIcon } from 'tdesign-icons-vue-next';

// API
import { getBackupDownloadUrl, getInstanceBackupFiles, postDeleteBackupFiles } from '@/api/instance';
import { type InstanceBackupFilesModel } from '@/api/model/instance';

const route = useRoute();

// 实例 ID
const instanceId = computed(() => {
  const idStr = route.params.serverId as string;
  if (!idStr) return NaN;
  return parseInt(idStr);
});

// 数据状态
const loading = ref(false);
const tableData = ref<InstanceBackupFilesModel[]>([]);
const selectedRowKeys = ref<string[]>([]); // 存储选中行的 rowKey

// 表格列
const columns: PrimaryTableCol<TableRowData>[] = [
  {
    colKey: 'row-select',
    type: 'multiple',
    width: 20,
    fixed: 'left',
  },
  {
    colKey: 'fileName',
    title: '文件名',
    ellipsis: true,
    width: 200,
  },
  {
    colKey: 'fileSizeStr',
    title: '文件大小',
    width: 120,
  },
  {
    colKey: 'createTime',
    title: '备份时间',
    width: 180,
  },
  {
    colKey: 'op',
    title: '操作',
    fixed: 'right',
    width: 140,
  },
];

// 延迟
const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

// 初始化数据
const initData = async () => {
  if (!instanceId.value) return;

  loading.value = true;
  selectedRowKeys.value = [];
  try {
    const res = await getInstanceBackupFiles(instanceId.value);
    tableData.value = (res as any).data || res;
  } catch (e: any) {
    MessagePlugin.error('获取备份列表失败: ' + e.message);
  } finally {
    loading.value = false;
  }
};

// --- 选择逻辑 ---
const onSelectChange = (value: (string | number)[]) => {
  selectedRowKeys.value = value as string[];
};

// --- 单个操作逻辑 ---

const handleDelete = (row: InstanceBackupFilesModel) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除备份?',
    body: `您确定要永久删除文件 "${row.fileName}" 吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      confirmDialog.hide();
      try {
        await postDeleteBackupFiles(instanceId.value, row.fileName);
        MessagePlugin.success('删除成功');
        await initData();
      } catch (e: any) {
        MessagePlugin.error(e.message || '删除失败');
      }
    },
    onClose: () => {
      confirmDialog.hide();
    },
  });
};

const handleDownload = (row: InstanceBackupFilesModel) => {
  try {
    const url = getBackupDownloadUrl(instanceId.value, row.fileName);
    window.open(url, '_blank');
  } catch (e: any) {
    MessagePlugin.error('下载失败！' + e.message);
  }
};

// --- 批量操作逻辑 ---

// 批量下载
const handleBatchDownload = () => {
  const count = selectedRowKeys.value.length;
  if (count === 0) return;

  MessagePlugin.info(`开始下载 ${count} 个文件，请注意允许浏览器弹窗...`);

  selectedRowKeys.value.forEach((fileName, index) => {
    setTimeout(() => {
      const url = getBackupDownloadUrl(instanceId.value, fileName);
      window.open(url, '_blank');
    }, index * 1000);
  });
};

// 批量删除
// 批量删除
const handleBatchDelete = () => {
  const count = selectedRowKeys.value.length;
  if (count === 0) return;

  const confirmDialog = DialogPlugin.confirm({
    header: '确认批量删除?',
    body: `您选中了 ${count} 个备份文件。删除后无法恢复，确定要继续吗？`,
    theme: 'danger',
    onConfirm: async () => {
      confirmDialog.hide();

      loading.value = true;
      let successCount = 0;
      let failCount = 0;
      let msgInstance: any = null;

      try {
        // 遍历删除
        for (const [index, fileName] of selectedRowKeys.value.entries()) {
          if (msgInstance) {
            MessagePlugin.close(msgInstance);
          }

          msgInstance = MessagePlugin.loading(`正在删除 ${fileName} (${index + 1}/${count})...`, 0);


          try {
            await postDeleteBackupFiles(instanceId.value, fileName);
            successCount++;
          } catch (e) {
            failCount++;
            console.error(`删除 ${fileName} 失败`, e);
          }

          // 服务器喝杯卡布奇诺
          if (index < count - 1) {
            await delay(800);
          }
        }
      } finally {
        if (msgInstance) {
          MessagePlugin.close(msgInstance);
        }

        loading.value = false;

        if (failCount === 0) {
          MessagePlugin.success(`批量删除完成，共删除 ${successCount} 个文件`);
        } else {
          MessagePlugin.warning(`批量操作完成：成功 ${successCount} 个，失败 ${failCount} 个`);
        }

        // 刷新列表
        await initData();
      }
    },
    onClose: () => {
      confirmDialog.hide();
    },
  });
};

// 监听路由变化
watch(
  () => route.params.serverId,
  (newId) => {
    if (newId) {
      initData();
    }
  },
);

onMounted(() => {
  initData();
});
</script>

<template>
  <div class="settings-container">
    <div class="setting-group-title">备份管理</div>

    <div class="setting-item">
      <div class="setting-info">
        <div class="title">存档快照</div>
        <div class="desc">
          查看和管理服务器的自动或手动备份。建议定期下载重要备份到本地保存。
          <br />
          当前共有 <b>{{ tableData.length }}</b> 个备份文件。最大保存备份文件的数量需要在实例设置中配置。
        </div>
      </div>

      <div class="setting-control">
        <transition name="fade" mode="out-in">
          <div v-if="selectedRowKeys.length > 0" class="bulk-action-bar">
            <div class="bar-separator"></div>
            <span class="selected-text">已选 {{ selectedRowKeys.length }} 项</span>

            <t-button variant="text" theme="primary" @click="handleBatchDownload"> 下载 </t-button>
            <t-button variant="text" theme="danger" @click="handleBatchDelete"> 删除 </t-button>
          </div>

          <t-button v-else theme="primary" variant="outline" @click="initData">
            <template #icon><refresh-icon /></template>
            刷新列表
          </t-button>
        </transition>
      </div>
    </div>

    <div class="backup-table-wrapper">
      <t-table
        row-key="fileName"
        :data="tableData"
        :columns="columns"
        :loading="loading"
        :selected-row-keys="selectedRowKeys"
        stripe
        hover
        class="custom-table"
        @select-change="onSelectChange"
      >
        <template #fileName="{ row }">
          <div class="file-name-cell">
            <file-icon style="margin-right: 6px; color: var(--td-brand-color)" />
            <span>{{ row.fileName }}</span>
          </div>
        </template>

        <template #createTime="{ row }">
          <div class="time-cell">
            <time-icon style="margin-right: 6px; color: var(--td-text-color-placeholder)" />
            {{ row.createTime }}
          </div>
        </template>

        <template #op="{ row }">
          <t-space>
            <t-tooltip content="下载备份">
              <t-button variant="text" shape="square" theme="primary" @click="handleDownload(row)">
                <download-icon />
              </t-button>
            </t-tooltip>
            <t-tooltip content="删除备份">
              <t-button variant="text" shape="square" theme="danger" @click="handleDelete(row)">
                <delete-icon />
              </t-button>
            </t-tooltip>
          </t-space>
        </template>

        <template #empty>
          <div class="empty-state">暂无备份记录</div>
        </template>
      </t-table>
    </div>
  </div>
</template>

<style scoped lang="less">
.settings-container {
  margin: 0 auto;
}

.setting-group-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  margin-top: 32px;
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);
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

.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 32px 16px 0;
  flex-wrap: wrap;

  .setting-info {
    flex: 1;
    padding-right: 32px;
    min-width: 200px;

    .title {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 500;
      line-height: 22px;
    }

    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
      line-height: 20px;
    }
  }

  .setting-control {
    width: auto;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    min-height: 32px;
  }
}

.bulk-action-bar {
  display: flex;
  align-items: center;
  background-color: var(--td-bg-color-secondarycontainer);
  padding: 4px 12px;
  border-radius: var(--td-radius-default);
  gap: 12px;
  animation: fadeIn 0.2s ease-in-out;

  .bar-separator {
    width: 2px;
    height: 14px;
    background-color: var(--td-component-stroke);
    margin-right: -4px;
  }

  .selected-text {
    font-size: 14px;
    color: var(--td-text-color-primary);
    font-weight: 500;
  }

  :deep(.t-button) {
    padding: 0 8px;
    height: 24px;
    line-height: 24px;
    font-size: 14px;
  }
}

/* --- 表格样式 --- */
.backup-table-wrapper {
  margin-top: 16px;
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  overflow: hidden;
}

.file-name-cell {
  display: flex;
  align-items: center;
  font-family: 'Consolas', monospace;
  font-size: 13px;
  word-break: break-all;
}

.time-cell {
  display: flex;
  align-items: center;
  color: var(--td-text-color-secondary);
}

.empty-state {
  padding: 32px;
  text-align: center;
  color: var(--td-text-color-placeholder);
}

/* 简单的淡入淡出动画 */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;
    align-items: flex-start;

    .setting-info {
      padding-right: 0;
      margin-bottom: 12px;
    }

    .setting-control {
      width: 100%;
      justify-content: flex-start;
      margin-top: 8px;
    }
  }
}
</style>
