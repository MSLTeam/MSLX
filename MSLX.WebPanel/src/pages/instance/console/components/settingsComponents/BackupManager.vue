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
  <div class="flex flex-col mx-auto">

    <div class="flex items-center gap-2 mt-5 mb-4 pb-2 border-b border-zinc-200/60 dark:border-zinc-700/60">
      <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
      <h2 class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0">备份管理</h2>
    </div>

    <div class="flex flex-col md:flex-row md:justify-between md:items-center py-4 pr-0 md:pr-8 gap-4 md:gap-8">

      <div class="flex-1 min-w-[200px]">
        <div class="text-sm font-bold text-zinc-800 dark:text-zinc-200 leading-snug">存档快照</div>
        <div class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 leading-relaxed">
          查看和管理服务器的自动或手动备份。建议定期下载重要备份到本地保存。
          <br />
          当前共有 <b class="text-zinc-700 dark:text-zinc-300">{{ tableData.length }}</b> 个备份文件。最大保存备份文件的数量需要在实例设置中配置。
        </div>
      </div>

      <div class="shrink-0 flex items-center min-h-[32px] w-full md:w-auto">
        <transition
          enter-active-class="transition-opacity duration-200 ease-out"
          enter-from-class="opacity-0"
          leave-active-class="transition-opacity duration-200 ease-in"
          leave-to-class="opacity-0"
          mode="out-in"
        >
          <div v-if="selectedRowKeys.length > 0" class="flex items-center bg-zinc-100 dark:bg-zinc-800/80 px-3 py-1.5 rounded-lg gap-3 shadow-sm w-full md:w-auto justify-between md:justify-start">
            <div class="flex items-center gap-3">
              <div class="w-[2px] h-[14px] bg-zinc-300 dark:bg-zinc-600 -mr-1"></div>
              <span class="text-sm font-bold text-zinc-800 dark:text-zinc-200">已选 {{ selectedRowKeys.length }} 项</span>
            </div>

            <div class="flex items-center">
              <t-button variant="text" theme="primary" class="!px-2 !h-7 !text-sm hover:!bg-[var(--color-primary)]/10 !rounded-md" @click="handleBatchDownload"> 下载 </t-button>
              <t-button variant="text" theme="danger" class="!px-2 !h-7 !text-sm hover:!bg-red-500/10 !rounded-md" @click="handleBatchDelete"> 删除 </t-button>
            </div>
          </div>

          <t-button v-else theme="primary" variant="outline" class="!rounded-lg shadow-sm w-full md:w-auto" @click="initData">
            <template #icon><refresh-icon /></template> 刷新列表
          </t-button>
        </transition>
      </div>
    </div>

    <div class="mt-4 border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl overflow-hidden shadow-sm bg-white/50 dark:bg-zinc-900/20">
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
          <div class="flex items-center font-mono text-[13px] text-zinc-700 dark:text-zinc-300 break-all">
            <file-icon class="mr-1.5 text-[var(--color-primary)]" />
            <span>{{ row.fileName }}</span>
          </div>
        </template>

        <template #createTime="{ row }">
          <div class="flex items-center text-zinc-500 dark:text-zinc-400 text-[13px]">
            <time-icon class="mr-1.5" />
            {{ row.createTime }}
          </div>
        </template>

        <template #op="{ row }">
          <t-space>
            <t-tooltip content="下载备份">
              <t-button variant="text" shape="square" theme="primary" class="!rounded-md hover:!bg-[var(--color-primary)]/10 transition-colors" @click="handleDownload(row)">
                <download-icon />
              </t-button>
            </t-tooltip>
            <t-tooltip content="删除备份">
              <t-button variant="text" shape="square" theme="danger" class="!rounded-md hover:!bg-red-500/10 transition-colors" @click="handleDelete(row)">
                <delete-icon />
              </t-button>
            </t-tooltip>
          </t-space>
        </template>

        <template #empty>
          <div class="p-8 text-center text-sm font-medium text-zinc-400 dark:text-zinc-500">暂无备份记录</div>
        </template>
      </t-table>
    </div>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
