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
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">

    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 backdrop-blur-md rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">实例备份管理</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">
          管理所有服务器实例的本地备份文件
        </p>
      </div>

      <div class="flex items-center gap-3">
        <t-button variant="dashed" :loading="loading" @click="fetchData">
          <template #icon><refresh-icon /></template>
          刷新列表
        </t-button>
      </div>
    </div>

    <div class="relative min-h-[400px]">

      <div v-if="loading && instanceList.length === 0" class="flex justify-center items-center py-24">
        <t-loading text="加载数据中..." size="small"></t-loading>
      </div>

      <template v-else-if="sortedInstances.length > 0">
        <div class="flex flex-col gap-5">
          <div
            v-for="(instance, index) in sortedInstances"
            :key="instance.id"
            class="list-item-anim"
            :style="{ animationDelay: `${index * 0.05}s` }"
          >
            <div
              class="design-card flex flex-col bg-[var(--td-bg-color-container)]/80 backdrop-blur-md rounded-2xl border border-[var(--td-component-border)] shadow-sm transition-all duration-300 hover:border-[var(--color-primary)]/30"
              :class="{ 'opacity-80': !instance.backups?.length }"
            >
              <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 pb-0 border-b-0">
                <div class="flex flex-col sm:flex-row sm:items-center gap-3">
                  <t-tag theme="primary" variant="light" shape="round" class="!px-3 !font-mono font-bold tracking-wider">ID: {{ instance.id }}</t-tag>
                  <div class="flex items-center gap-3">
                    <h3 class="text-base font-bold text-[var(--td-text-color-primary)] flex items-center gap-2 m-0 tracking-tight">
                      <server-icon class="text-[var(--td-text-color-secondary)] shrink-0" />
                      {{ instance.name }}
                    </h3>
                    <span class="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-zinc-100 dark:bg-zinc-800 text-xs text-[var(--td-text-color-secondary)] font-medium border border-[var(--td-component-border)]">
                      <cloud-icon size="14px" class="opacity-80" />
                      {{ instance.core }}
                    </span>
                  </div>
                </div>

                <div class="flex items-center gap-2 mt-2 sm:mt-0">
                  <t-tag v-if="instance.backups?.length" theme="success" variant="light" shape="round" class="!px-3 !font-medium">
                    {{ instance.backups.length }} 个备份
                  </t-tag>
                  <t-tag v-else theme="default" variant="light" shape="round" class="!px-3 !text-zinc-400 !bg-zinc-100 dark:!bg-zinc-800">无备份</t-tag>
                </div>
              </div>

              <div class="mx-5 mt-4 bg-zinc-50/80 dark:bg-zinc-800/50 rounded-xl border border-[var(--td-component-border)] overflow-hidden transition-all">
                <div
                  class="flex items-center gap-2 p-2.5 px-4 cursor-pointer text-[var(--td-text-color-secondary)] hover:text-zinc-800 dark:hover:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors"
                  @click="togglePath(instance.id)"
                >
                  <folder-open-icon class="opacity-80" size="18px" />
                  <span class="text-xs font-medium select-none">存储路径</span>
                  <component :is="expandedPaths.has(instance.id) ? ChevronUpIcon : ChevronDownIcon" class="ml-auto opacity-70" />
                </div>
                <div
                  v-show="expandedPaths.has(instance.id)"
                  class="p-3 px-4 text-xs font-mono text-[var(--td-text-color-secondary)] break-all border-t border-[var(--td-component-border)] bg-zinc-100/50 dark:bg-zinc-900/30 shadow-inner"
                >
                  {{ instance.backupPath }}
                </div>
              </div>

              <div class="p-5 pt-4">
                <div v-if="instance.backups?.length" class="flex flex-col gap-3">

                  <div v-if="selectedRowKeys[instance.id]?.length > 0" class="flex items-center gap-3 p-2 px-4 bg-red-500/10 border border-red-500/20 rounded-xl mb-1 transition-all">
                    <span class="text-xs font-medium text-red-600 dark:text-red-400">已选 {{ selectedRowKeys[instance.id].length }} 项</span>
                    <t-button theme="danger" variant="text" size="small" class="!h-auto !py-1 hover:!bg-red-500/20" @click="handleBatchDelete(instance.id)">
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
                      <div class="flex items-center gap-2">
                        <span class="text-[10px] font-extrabold bg-[var(--color-primary)]/10 text-[var(--color-primary)] px-1.5 py-0.5 rounded border border-[var(--color-primary)]/20 shrink-0 tracking-wider">ZIP</span>
                        <span class="font-medium text-[var(--td-text-color-primary)] truncate" :title="row.fileName">{{ row.fileName }}</span>
                      </div>
                    </template>

                    <template #op="{ row }">
                      <div class="flex items-center gap-1">
                        <t-button
                          theme="primary"
                          variant="text"
                          size="small"
                          class="hover:!bg-[var(--color-primary)]/10"
                          @click="handleDownload(instance.id, row.fileName)"
                        >
                          <template #icon><download-icon /></template>
                          下载
                        </t-button>
                        <t-button
                          theme="danger"
                          variant="text"
                          size="small"
                          class="hover:!bg-red-500/10"
                          @click="handleDeleteOne(instance.id, row.fileName)"
                        >
                          <template #icon><delete-icon /></template>
                          删除
                        </t-button>
                      </div>
                    </template>
                  </t-table>
                </div>

                <div v-else class="flex flex-col items-center justify-center py-10">
                  <span class="text-sm font-medium text-[var(--td-text-color-secondary)] bg-zinc-50 dark:bg-zinc-800/50 px-4 py-2 rounded-full border border-[var(--td-component-border)]">
                    当前实例暂无备份文件
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>

      <div
        v-else
        class="flex flex-col items-center justify-center py-24 bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-[var(--td-component-border)]"
      >
        <t-empty class="!bg-transparent" description="尚未发现任何实例" />
      </div>
    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
