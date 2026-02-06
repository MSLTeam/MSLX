<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  CloudIcon,
  ServerIcon,
  FolderOpenIcon,
  DownloadIcon,
  DeleteIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  RefreshIcon,
} from 'tdesign-icons-vue-next';
import { getAllInstanceBackupFiles, postDeleteBackupFiles, getBackupDownloadUrl } from '@/api/instance';
import { AllInstanceBackupFilesModel } from '@/api/model/instance';


const loading = ref(false);
const instanceList = ref<AllInstanceBackupFilesModel[]>([]);
const expandedPaths = ref<Set<number>>(new Set()); // 控制路径折叠
const selectedRowKeys = ref<Record<number, string[]>>({}); // 存储每个实例选中的文件名为 Key

// 表格列
const columns = [
  { colKey: 'row-select', type: 'multiple', width: 30, fixed: 'left' },
  { colKey: 'fileName', title: '文件名', ellipsis: true },
  { colKey: 'fileSizeStr', title: '大小', width: 100 },
  { colKey: 'createTime', title: '创建时间', width: 180 },
  { colKey: 'op', title: '操作', width: 140, fixed: 'right' },
];


const sortedInstances = computed(() => {
  const list = [...instanceList.value];
  return list.sort((a, b) => {
    const aHas = a.backups && a.backups.length > 0;
    const bHas = b.backups && b.backups.length > 0;
    // 有备份的排前面
    if (aHas && !bHas) return -1;
    if (!aHas && bHas) return 1;
    return a.id - b.id;
  });
});


// 获取数据
const fetchData = async () => {
  loading.value = true;
  try {
    const res = await getAllInstanceBackupFiles();
    instanceList.value = res || [];
  } catch (error) {
    MessagePlugin.error('获取备份列表失败');
    console.error(error);
  } finally {
    loading.value = false;
  }
};

// 切换路径显示/隐藏
const togglePath = (id: number) => {
  if (expandedPaths.value.has(id)) {
    expandedPaths.value.delete(id);
  } else {
    expandedPaths.value.add(id);
  }
};

// 处理下载
const handleDownload = (id: number, fileName: string) => {
  const url = getBackupDownloadUrl(id, fileName);
  window.open(url, '_blank');
};

// 删除逻辑
const processDelete = async (instanceId: number, fileNames: string[]) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除',
    body: `确定要删除选中的 ${fileNames.length} 个备份文件吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      confirmDialog.hide();
      const msg = MessagePlugin.loading('正在删除中...');

      try {
        // 并发调用接口
        const promises = fileNames.map((fileName) => postDeleteBackupFiles(instanceId, fileName));

        await Promise.all(promises);

        MessagePlugin.success('删除成功');
        // 清空该实例的选中状态
        if (selectedRowKeys.value[instanceId]) {
          selectedRowKeys.value[instanceId] = [];
        }
        // 刷新列表
        await fetchData();
      } catch (error) {
        MessagePlugin.error('部分文件删除失败，请重试 ' + error.message);
      } finally {
        MessagePlugin.close(msg);
      }
    },
  });
};

// 单个删除
const handleDeleteOne = (instanceId: number, fileName: string) => {
  processDelete(instanceId, [fileName]);
};

// 批量删除
const handleBatchDelete = (instanceId: number) => {
  const selected = selectedRowKeys.value[instanceId];
  if (!selected || selected.length === 0) {
    MessagePlugin.warning('请先选择要删除的文件');
    return;
  }
  processDelete(instanceId, selected);
};

// 处理表格选中变化
const onSelectChange = (value: string[], { _row }: any, instanceId: number) => {
  selectedRowKeys.value = {
    ...selectedRowKeys.value,
    [instanceId]: value,
  };
};

onMounted(() => {
  fetchData();
});
</script>

<template>
  <div class="backup-page-container">
    <div class="page-header">
      <div class="title-area">
        <h2 class="page-title">实例备份管理</h2>
        <p class="page-desc">管理所有服务器实例的本地备份文件</p>
      </div>
      <t-button theme="primary" variant="text" :loading="loading" @click="fetchData">
        <template #icon><refresh-icon /></template>
        刷新列表
      </t-button>
    </div>

    <div v-if="loading && instanceList.length === 0" class="loading-wrapper">
      <t-loading text="加载数据中..." size="small"></t-loading>
    </div>

    <div v-else class="card-list">
      <transition-group name="list-anim">
        <div v-for="instance in sortedInstances" :key="instance.id" class="instance-card-wrapper">
          <t-card :bordered="false" class="instance-card" :class="{ 'is-empty': !instance.backups?.length }">
            <div class="card-header">
              <div class="header-left">
                <t-tag theme="primary" variant="light" shape="mark">ID: {{ instance.id }}</t-tag>
                <div class="instance-info">
                  <h3 class="instance-name">
                    <server-icon class="icon-mr" />
                    {{ instance.name }}
                  </h3>
                  <span class="instance-core">
                    <cloud-icon class="icon-mr" />
                    {{ instance.core }}
                  </span>
                </div>
              </div>

              <div class="header-right">
                <t-tag v-if="instance.backups?.length" theme="success" variant="outline">
                  {{ instance.backups.length }} 个备份
                </t-tag>
                <t-tag v-else theme="default" disabled>无备份</t-tag>
              </div>
            </div>

            <div class="path-section">
              <div class="path-toggle" @click="togglePath(instance.id)">
                <folder-open-icon />
                <span class="label">存储路径</span>
                <component :is="expandedPaths.has(instance.id) ? ChevronUpIcon : ChevronDownIcon" class="toggle-icon" />
              </div>
              <div v-show="expandedPaths.has(instance.id)" class="path-content">
                {{ instance.backupPath }}
              </div>
            </div>

            <div v-if="instance.backups?.length" class="backup-table-area">
              <div v-if="selectedRowKeys[instance.id]?.length > 0" class="table-toolbar">
                <span class="selected-count">已选 {{ selectedRowKeys[instance.id].length }} 项</span>
                <t-button theme="danger" variant="text" size="small" @click="handleBatchDelete(instance.id)">
                  批量删除
                </t-button>
              </div>

              <t-table
                row-key="fileName"
                :data="instance.backups"
                :columns="columns as any"
                :selected-row-keys="selectedRowKeys[instance.id] || []"
                size="small"
                :hover="true"
                :pagination="instance.backups.length > 5 ? { pageSize: 5 } : null"
                @select-change="(val, ctx) => onSelectChange(val as any, ctx, instance.id)"
              >
                <template #fileName="{ row }">
                  <div class="file-name-cell">
                    <span class="zip-icon">ZIP</span>
                    <span class="text" :title="row.fileName">{{ row.fileName }}</span>
                  </div>
                </template>

                <template #op="{ row }">
                  <div class="op-buttons">
                    <t-button
                      theme="primary"
                      variant="text"
                      size="small"
                      @click="handleDownload(instance.id, row.fileName)"
                    >
                      <template #icon><download-icon /></template>
                      下载
                    </t-button>
                    <t-button
                      theme="danger"
                      variant="text"
                      size="small"
                      @click="handleDeleteOne(instance.id, row.fileName)"
                    >
                      <template #icon><delete-icon /></template>
                      删除
                    </t-button>
                  </div>
                </template>
              </t-table>
            </div>

            <div v-else class="empty-backups">
              <span class="empty-text">当前实例暂无备份文件</span>
            </div>
          </t-card>
        </div>
      </transition-group>
    </div>
  </div>
</template>

<style lang="less" scoped>
@card-bg: var(--td-bg-color-container);
@text-primary: var(--td-text-color-primary);
@text-secondary: var(--td-text-color-secondary);
@border-level: var(--td-component-stroke);

.backup-page-container {
  padding: 12px;
  margin: 0 auto;
  min-height: 100%;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;

  .title-area {
    .page-title {
      font-size: 24px;
      font-weight: 700;
      color: @text-primary;
      margin: 0 0 8px 0;
    }
    .page-desc {
      color: @text-secondary;
      font-size: 14px;
      margin: 0;
    }
  }
}

.loading-wrapper {
  display: flex;
  justify-content: center;
  padding: 48px;
}

.card-list {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

// 实例卡片样式
.instance-card {
  transition: all 0.3s ease;
  background: @card-bg;

  // 无备份时的样式降级
  &.is-empty {
    opacity: 0.8;
    .card-header {
      border-bottom: none;
    }
  }

  // 头部布局
  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding-bottom: 16px;
    border-bottom: 1px solid @border-level;
    flex-wrap: wrap;
    gap: 12px;

    .header-left {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;

      .instance-info {
        display: flex;
        align-items: baseline;
        gap: 12px;

        .instance-name {
          margin: 0;
          font-size: 18px;
          display: flex;
          align-items: center;
          color: @text-primary;
        }

        .instance-core {
          font-size: 13px;
          color: @text-secondary;
          background: var(--td-bg-color-secondarycontainer);
          padding: 2px 8px;
          border-radius: 4px;
          display: flex;
          align-items: center;
        }
      }
    }
  }

  // 路径折叠区
  .path-section {
    margin-top: 12px;
    background: var(--td-bg-color-secondarycontainer);
    border-radius: 6px;
    overflow: hidden;

    .path-toggle {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      cursor: pointer;
      color: @text-secondary;
      font-size: 13px;
      user-select: none;
      transition: background 0.2s;

      &:hover {
        background: var(--td-bg-color-component-hover);
        color: @text-primary;
      }

      .toggle-icon {
        margin-left: auto;
      }
    }

    .path-content {
      padding: 8px 12px;
      font-family: monospace;
      font-size: 12px;
      color: @text-secondary;
      word-break: break-all;
      border-top: 1px solid rgba(0, 0, 0, 0.05);
      background: rgba(0, 0, 0, 0.02);
    }
  }

  // 表格区域
  .backup-table-area {
    margin-top: 16px;

    .table-toolbar {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 8px;
      padding: 4px 8px;
      background: var(--td-error-color-1);
      border-radius: 4px;

      .selected-count {
        font-size: 12px;
        color: var(--td-error-color-7);
      }
    }

    .file-name-cell {
      display: flex;
      align-items: center;
      gap: 8px;

      .zip-icon {
        font-size: 10px;
        background: var(--td-brand-color-focus);
        color: var(--td-brand-color);
        padding: 1px 4px;
        border-radius: 3px;
        font-weight: bold;
      }
      .text {
        font-weight: 500;
      }
    }

    .op-buttons {
      display: flex;
      gap: 8px;
    }
  }

  .empty-backups {
    padding: 32px 0;
    text-align: center;
    color: var(--td-text-color-disabled);
    font-size: 13px;
  }
}

// 通用图标边距
.icon-mr {
  margin-right: 6px;
}

// 动画
.list-anim-move,
.list-anim-enter-active,
.list-anim-leave-active {
  transition: all 0.5s ease;
}
.list-anim-enter-from,
.list-anim-leave-to {
  opacity: 0;
  transform: translateY(20px);
}

// 移动端特定适配
@media (max-width: 600px) {
  .instance-card {
    .header-left {
      flex-direction: column;
      align-items: flex-start !important;
      gap: 8px !important;
    }
  }
}
</style>
