<script setup lang="ts">
import { ref, onMounted, reactive, computed } from 'vue';
import { DownloadIcon, TimeIcon, LinkIcon, ChevronLeftIcon, ChevronRightIcon } from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import { getMarketPluginList, getMarketPluginVersions, postInstallPlugin, getInstallPluginStatus } from '@/api/plugins';
import type { MarketPluginModel, MarketPluginVersionModel } from '@/api/model/plugins';
import NotificationPlugin from 'tdesign-vue-next/es/notification/plugin';

const listLoading = ref(false);
const keyword = ref('');
const marketList = ref<MarketPluginModel[]>([]);
const pagination = reactive({ current: 1, pageSize: 10, total: 0 });

// 搜索
const handleSearch = (kw: string) => {
  keyword.value = kw;
  pagination.current = 1;
  fetchMarketList();
};
defineExpose({ handleSearch });

// 加载市场列表
const fetchMarketList = async () => {
  listLoading.value = true;
  try {
    const res = await getMarketPluginList({
      keyword: keyword.value,
      page: pagination.current,
      size: pagination.pageSize,
    });
    marketList.value = res.list;
    pagination.total = res.total;
  } catch (error: any) {
    MessagePlugin.error('获取市场列表失败: ' + error.message);
  } finally {
    listLoading.value = false;
  }
};

// 分页逻辑
const totalPages = computed(() => Math.ceil(pagination.total / pagination.pageSize) || 1);
const pageNumbers = computed(() => {
  const current = pagination.current;
  const total = totalPages.value;
  const delta = 2;
  let pages: (number | string)[] = [];

  for (let i = Math.max(2, current - delta); i <= Math.min(total - 1, current + delta); i++) {
    pages.push(i);
  }
  if (current - delta > 2) pages.unshift('...');
  if (current + delta < total - 1) pages.push('...');

  if (total > 1) {
    pages.unshift(1);
    pages.push(total);
  } else {
    pages = [1];
  }
  return pages;
});

const changePage = (p: number | string) => {
  if (typeof p !== 'number') return;
  if (p < 1 || p > totalPages.value || p === pagination.current) return;
  pagination.current = p;
  fetchMarketList();
  window.scrollTo({ top: 0, behavior: 'smooth' });
};

// 版本对话框 & 下载状态
const versionDialogVisible = ref(false);
const activePlugin = ref<MarketPluginModel | null>(null);
const versionsLoading = ref(false);
const versionsList = ref<MarketPluginVersionModel[]>([]);

const installState = reactive({ isInstalling: false, progress: 0, message: '', timer: null as any });

const openVersionDialog = async (plugin: MarketPluginModel) => {
  activePlugin.value = plugin;
  versionDialogVisible.value = true;
  versionsLoading.value = true;
  versionsList.value = [];

  try {
    const res = await getMarketPluginVersions(plugin.appId, { page: 1, size: 20 });
    versionsList.value = res.list;
  } catch (error: any) {
    MessagePlugin.error('获取版本列表失败: ' + error.message);
    versionDialogVisible.value = false;
  } finally {
    versionsLoading.value = false;
  }
};

const handleInstallVersion = async (version: MarketPluginVersionModel) => {
  if (!activePlugin.value) return;
  installState.isInstalling = true;
  installState.progress = 0;
  installState.message = '正在请求下载...';

  const formatFileName = (appId: string) => {
    return (
      appId
        .split('-')
        .map((word) => (word.toLowerCase() === 'mslx' ? 'MSLX' : word.charAt(0).toUpperCase() + word.slice(1)))
        .join('.') + '.dll'
    );
  };

  const safeFileName = formatFileName(activePlugin.value.appId).replace(/[^a-zA-Z0-9_\-.]/g, '');
  try {
    const res = await postInstallPlugin(version.downloadLink, safeFileName, true);
    pollInstallStatus(res.taskId);
  } catch (error: any) {
    installState.isInstalling = false;
    MessagePlugin.error(`提交安装失败: ${error.message}`);
  }
};

const pollInstallStatus = (taskId: string) => {
  if (installState.timer) clearInterval(installState.timer);
  installState.timer = setInterval(async () => {
    try {
      const res = await getInstallPluginStatus(taskId);
      installState.progress = res.progress;
      installState.message = res.message;

      if (res.status === 'success') {
        clearInterval(installState.timer);
        installState.isInstalling = false;
        versionDialogVisible.value = false;
        NotificationPlugin.success({
          title: '插件安装成功',
          content: '文件已就绪，将在下次重启时生效。',
          duration: 5000,
        });
      } else if (res.status === 'error') {
        clearInterval(installState.timer);
        installState.isInstalling = false;
        MessagePlugin.error(`安装失败: ${res.message}`);
      }
    } catch (error: any) {
      clearInterval(installState.timer);
      installState.isInstalling = false;
      MessagePlugin.error(`查询进度异常: ${error.message}`);
    }
  }, 1000);
};

const onDialogClose = () => {
  if (installState.isInstalling) MessagePlugin.warning('安装正在后台进行');
  if (installState.timer) clearInterval(installState.timer);
  installState.isInstalling = false;
};

onMounted(() => {
  fetchMarketList();
});
</script>

<template>
  <div class="relative min-h-[400px]">
    <div v-if="listLoading" class="flex flex-col items-center justify-center py-24">
      <t-loading size="medium" text="正在从 MSLX 星系加载插件生态..." />
    </div>

    <div
      v-else-if="marketList.length === 0"
      class="py-24 text-center design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-[var(--td-component-border)]"
    >
      <div class="text-[var(--td-text-color-secondary)]">星空浩瀚，未找到相关插件。换个词试试？</div>
    </div>

    <div v-else class="flex flex-col">
      <div class="flex flex-col gap-4 mb-8">
        <div
          v-for="(item, index) in marketList"
          :key="item.appId"
          :style="{ animationDelay: `${index * 0.05}s` }"
          class="list-item-anim design-card flex flex-col lg:flex-row lg:items-center gap-5 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-primary/40 transition-all duration-300"
        >
          <div class="shrink-0 hidden sm:block">
            <t-avatar
              :image="item.icon || 'https://www.mslmc.cn/logo.png'"
              shape="round"
              size="64px"
              class="bg-white shadow-sm ring-1 ring-zinc-200 dark:ring-zinc-700"
            />
          </div>

          <div class="flex-grow min-w-0 flex flex-col gap-1.5">
            <div class="flex items-center gap-3">
              <h3 class="text-base font-bold text-[var(--td-text-color-primary)] truncate m-0 tracking-tight">
                {{ item.name }}
              </h3>
              <span
                class="text-xs text-[var(--td-text-color-secondary)] font-mono opacity-60 bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded shrink-0"
              >
                {{ item.appId }}
              </span>
            </div>
            <p class="text-sm text-[var(--td-text-color-secondary)] line-clamp-2 m-0 leading-relaxed max-w-2xl">
              {{ item.shortDesc }}
            </p>
          </div>

          <div
            class="shrink-0 flex items-center gap-6 lg:px-6 lg:border-l border-dashed border-zinc-200 dark:border-zinc-700/60 mt-3 lg:mt-0 pt-4 lg:pt-0 border-t lg:border-t-0 w-full lg:w-auto"
          >
            <div class="flex flex-col gap-1.5 min-w-[120px]">
              <span
                class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                >Developer</span
              >
              <div class="flex items-center gap-2.5">
                <t-avatar :image="item.developerAvatar" size="32px" shape="circle" class="ring-2 ring-primary/20" />
                <span class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[100px]">{{
                  item.developerName
                }}</span>
              </div>
            </div>

            <div class="flex flex-col gap-1.5">
              <span
                class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                >Downloads</span
              >
              <div class="flex items-center gap-1.5 text-sm font-bold text-[var(--td-text-color-primary)]">
                <download-icon class="text-primary/70" /> {{ item.totalDownloads }}
              </div>
            </div>
          </div>

          <div class="shrink-0 flex items-center gap-2.5 mt-4 lg:mt-0 ml-auto w-full lg:w-auto justify-end">
            <t-button
              variant="outline"
              shape="round"
              :href="`https://mslx-plugins.mslmc.net/plugins/${item.appId}`"
              target="_blank"
              class="hover:!text-primary hover:!border-primary"
            >
              <template #icon><link-icon /></template> 查看详情
            </t-button>
            <t-button theme="primary" shape="round" @click="openVersionDialog(item)">
              <template #icon><download-icon /></template> 安装版本
            </t-button>
          </div>
        </div>
      </div>

      <div v-if="totalPages > 1" class="flex items-center justify-center gap-1.5 md:gap-2 mb-4">
        <button
          class="h-9 px-3 flex items-center gap-1 rounded-xl border border-[var(--td-component-border)] text-sm font-medium text-[var(--td-text-color-secondary)] hover:bg-[var(--td-bg-color-secondarycontainer)] hover:text-primary transition-colors disabled:opacity-40 disabled:cursor-not-allowed bg-white dark:bg-zinc-800/80 shadow-sm"
          @click="changePage(pagination.current - 1)"
          :disabled="pagination.current === 1"
        >
          <chevron-left-icon size="16px" /> 上一页
        </button>

        <div class="flex items-center gap-1">
          <button
            v-for="(p, index) in pageNumbers"
            :key="index"
            :disabled="p === '...'"
            @click="changePage(p)"
            :class="[
              'h-9 w-9 rounded-xl flex items-center justify-center text-sm font-bold transition-all duration-200',
              p === pagination.current
                ? 'bg-primary text-white shadow-md shadow-primary/30 transform scale-105'
                : p === '...'
                  ? 'text-zinc-400 cursor-default'
                  : 'text-[var(--td-text-color-secondary)] border border-transparent hover:border-[var(--td-component-border)] hover:bg-[var(--td-bg-color-secondarycontainer)] bg-white dark:bg-zinc-800/80 shadow-sm',
            ]"
          >
            {{ p }}
          </button>
        </div>

        <button
          @click="changePage(pagination.current + 1)"
          :disabled="pagination.current === totalPages"
          class="h-9 px-3 flex items-center gap-1 rounded-xl border border-[var(--td-component-border)] text-sm font-medium text-[var(--td-text-color-secondary)] hover:bg-[var(--td-bg-color-secondarycontainer)] hover:text-primary transition-colors disabled:opacity-40 disabled:cursor-not-allowed bg-white dark:bg-zinc-800/80 shadow-sm"
        >
          下一页 <chevron-right-icon size="16px" />
        </button>
      </div>
    </div>

    <t-dialog
      v-model:visible="versionDialogVisible"
      :header="activePlugin ? `安装插件: ${activePlugin.name}` : '版本选择'"
      width="640px"
      :footer="false"
      @close="onDialogClose"
    >
      <div class="min-h-[300px]">
        <div v-if="installState.isInstalling" class="py-12 flex flex-col items-center justify-center space-y-4">
          <div class="text-primary font-bold">{{ installState.message }}</div>
          <t-progress :percentage="installState.progress" :color="{ from: '#0052D9', to: '#00A870' }" class="w-3/4" />
          <div class="text-xs text-zinc-400 mt-2">下载过程中请勿关闭窗口...</div>
        </div>

        <template v-else>
          <div v-if="versionsLoading" class="py-20 flex justify-center">
            <t-loading size="small" text="正在获取可用版本..." />
          </div>
          <div v-else-if="versionsList.length === 0" class="py-20 text-center text-zinc-400">
            暂无可用版本，开发者可能还未上传。
          </div>
          <div v-else class="flex flex-col gap-3 max-h-[500px] overflow-y-auto pr-1 custom-scrollbar">
            <div
              v-for="ver in versionsList"
              :key="ver.id"
              class="border border-[var(--td-component-border)] rounded-xl p-4 hover:bg-zinc-50 dark:hover:bg-zinc-800/40 transition-colors"
            >
              <div class="flex items-center justify-between mb-2">
                <div class="flex items-center gap-2">
                  <t-tag theme="primary" variant="light">v{{ ver.versionName }}</t-tag>
                  <span class="text-xs text-[var(--td-text-color-secondary)] flex items-center gap-1"
                    ><time-icon /> {{ new Date(ver.createdAt).toLocaleDateString() }}</span
                  >
                </div>
                <t-button size="small" theme="primary" @click="handleInstallVersion(ver)"
                  ><template #icon><download-icon /></template> 安装此版本</t-button
                >
              </div>
              <div
                class="mt-2 bg-zinc-50 dark:bg-black/20 p-3 rounded-lg text-[var(--td-text-color-secondary)] whitespace-pre-wrap font-mono text-xs leading-relaxed max-h-32 overflow-y-auto custom-scrollbar"
              >
                {{ ver.changelog || '无版本更新说明。' }}
              </div>
            </div>
          </div>
        </template>
      </div>
    </t-dialog>
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
.line-clamp-2 {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
}
.design-card {
  transition: all 0.3s ease;
}
.custom-scrollbar::-webkit-scrollbar {
  width: 6px;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
  background-color: var(--td-scrollbar-color);
  border-radius: 4px;
}
</style>
