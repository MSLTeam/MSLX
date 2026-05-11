<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  UserIcon, LinkIcon, HomeIcon, ExtensionIcon, StoreIcon,
  PlayCircleIcon, StopCircleIcon, DeleteIcon, RollbackIcon
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import Result from '@/components/result/index.vue';
import { useUserStore } from '@/store';
import type { PluginListModel } from '@/api/model/plugins';
import { getPluginList, postPluginAction } from '@/api/plugins';

const userStore = useUserStore();
const loading = ref(true);
const actionLoading = ref(false);
const isError = ref(false);
const plugins = ref<PluginListModel[]>([]);

const emit = defineEmits(['go-market']);

async function getList() {
  try {
    loading.value = true;
    isError.value = false;
    const res = await getPluginList();
    plugins.value = res;
  } catch (error: any) {
    console.error(error);
    isError.value = true;
    MessagePlugin.error('获取插件列表失败: ' + error.message);
  } finally {
    loading.value = false;
  }
}

// 处理插件action
async function handleAction(id: string, action: string) {
  if (actionLoading.value) return;

  let loadingInstance: any = null;

  try {
    actionLoading.value = true;

    loadingInstance = await MessagePlugin.loading('正在处理...');

    await postPluginAction(id, action);

    MessagePlugin.success('操作成功');

    // 刷新
    await getList();
  } catch (error: any) {
    console.error(error);
    MessagePlugin.error('操作失败: ' + (error.message || '未知错误'));
  } finally {
    if (loadingInstance) {
      loadingInstance.close();
    }
    actionLoading.value = false;
  }
}

// 状态标签颜色映射
const getStatusTheme = (status: string) => {
  if (status === '已启用') return 'success';
  if (status === '已禁用') return 'default';
  if (status === '加载失败') return 'danger';
  if (status?.includes('下次重启')) return 'warning';
  return 'primary';
};

const resolveUrl = (path: string) => {
  if (!path) return '';
  if (path.startsWith('http')) return path;
  const { baseUrl } = userStore;
  const root = baseUrl || window.location.origin;
  return `${root}${path.startsWith('/') ? '' : '/'}${path}`;
};

defineExpose({ getList });

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="relative min-h-[400px]">
    <div v-if="loading && plugins.length === 0" class="flex flex-col items-center justify-center py-24">
      <t-loading size="medium" text="正在扫描本地已安装插件..." />
    </div>

    <div
      v-else-if="isError"
      class="flex flex-col items-center justify-center py-16 design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border border-red-500/20"
    >
      <result title="数据获取失败" tip="无法获取插件元数据，请检查服务状态" type="500">
        <t-button theme="primary" @click="getList">重试</t-button>
      </result>
    </div>

    <div
      v-else-if="plugins.length === 0"
      class="flex flex-col items-center justify-center py-24 design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-[var(--td-component-border)]"
    >
      <result title="暂无已安装的插件" tip="当前系统目录暂无扩展，前往插件市场发现更多功能" type="404">
        <t-button theme="primary" size="large" class="mt-2 !rounded-xl" @click="emit('go-market')">
          <template #icon><store-icon /></template>
          前往插件市场
        </t-button>
      </result>
    </div>

    <div v-else class="flex flex-col gap-4">
      <div
        v-for="(item, index) in plugins"
        :key="item.id"
        :style="{ animationDelay: `${index * 0.05}s` }"
        class="list-item-anim design-card group bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md transition-all duration-300 p-5"
      >
        <div class="flex flex-col md:flex-row items-start md:items-center gap-5">
          <div class="shrink-0">
            <div
              class="w-16 h-16 rounded-xl border border-[var(--td-component-border)] overflow-hidden bg-zinc-50 dark:bg-zinc-900/50 flex items-center justify-center shadow-inner group-hover:shadow-md transition-shadow relative"
            >
              <img
                v-if="item.icon"
                :src="resolveUrl(item.icon)"
                class="w-full h-full object-cover"
                @error="(e) => ((e.target as HTMLImageElement).src = 'https://www.mslmc.cn/logo.png')"
              />
              <extension-icon v-else size="32px" class="text-zinc-400" />
            </div>
          </div>

          <div class="flex-grow min-w-0 flex flex-col gap-1.5">
            <div class="flex items-center gap-3">
              <h3 class="text-base font-bold text-[var(--td-text-color-primary)] truncate m-0 tracking-tight">
                {{ item.name }}
              </h3>

              <!-- 动态状态标签 -->
              <t-tag
                size="small"
                :theme="getStatusTheme(item.status)"
                variant="light"
                class="!px-2 !rounded-md"
              >
                {{ item.status || '未知状态' }}
              </t-tag>

              <t-tag size="small" variant="outline" theme="default" class="!px-2 !rounded-md">
                v{{ item.version }}
              </t-tag>
              <span class="text-xs font-mono text-[var(--td-text-color-secondary)] opacity-60 truncate">ID: {{ item.id }}</span>
            </div>
            <p class="text-sm text-[var(--td-text-color-secondary)] m-0 leading-relaxed line-clamp-2">
              {{ item.description || '该插件暂无详细说明。' }}
            </p>
          </div>

          <div
            class="shrink-0 flex flex-wrap md:flex-nowrap items-center gap-6 md:pl-6 md:border-l border-dashed border-zinc-200 dark:border-zinc-700/60 mt-2 md:mt-0 pt-3 md:pt-0 border-t md:border-t-0 w-full md:w-auto"
          >
            <!-- 开发者信息 -->
            <div class="flex flex-col gap-1.5 min-w-[90px]">
              <span class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80">DEVELOPER</span>
              <div class="flex items-center gap-2 text-[var(--td-text-color-primary)]">
                <user-icon size="14px" class="text-[var(--color-primary)] opacity-70" />
                <a
                  v-if="item.authorUrl"
                  :href="item.authorUrl"
                  target="_blank"
                  class="text-sm font-bold hover:text-[var(--color-primary)] transition-colors cursor-pointer decoration-none truncate max-w-[100px]"
                >{{ item.developer }}</a>
                <span v-else class="text-sm font-bold truncate max-w-[100px]">{{ item.developer }}</span>
              </div>
            </div>

            <!-- 外链工具栏 -->
            <div class="flex items-center gap-2">
              <t-tooltip v-if="item.pluginUrl" content="插件主页">
                <t-button shape="square" variant="text" size="small" :href="item.pluginUrl" target="_blank" class="hover:!text-[var(--color-primary)] transition-colors">
                  <template #icon><link-icon /></template>
                </t-button>
              </t-tooltip>
              <t-tooltip v-if="item.authorUrl" content="开发者主页">
                <t-button shape="square" variant="text" size="small" :href="item.authorUrl" target="_blank" class="hover:!text-[var(--color-primary)] transition-colors">
                  <template #icon><home-icon /></template>
                </t-button>
              </t-tooltip>
            </div>

            <!-- 操作按钮区域 -->
            <div class="flex items-center gap-2 pl-4 border-l border-zinc-200 dark:border-zinc-700/60 ml-auto md:ml-0">

              <!-- 撤销操作 (仅在有待处理任务时显示) -->
              <t-button
                v-if="item.status?.includes('下次重启')"
                size="small"
                theme="default"
                variant="outline"
                :disabled="actionLoading"
                @click="handleAction(item.id, 'cancel')"
              >
                <template #icon><rollback-icon /></template>
                撤销
              </t-button>

              <!-- 启用 / 禁用 (互斥显示，在无待处理任务时显示) -->
              <template v-else>
                <t-button
                  v-if="item.status === '已禁用'"
                  size="small"
                  theme="primary"
                  variant="outline"
                  :disabled="actionLoading"
                  @click="handleAction(item.id, 'enable')"
                >
                  <template #icon><play-circle-icon /></template>
                  启用
                </t-button>
                <t-button
                  v-if="item.status === '已启用'"
                  size="small"
                  theme="warning"
                  variant="outline"
                  :disabled="actionLoading"
                  @click="handleAction(item.id, 'disable')"
                >
                  <template #icon><stop-circle-icon /></template>
                  禁用
                </t-button>
              </template>

              <!-- 删除按钮 (安全气泡确认) -->
              <t-popconfirm
                v-if="!item.status?.includes('下次重启删除')"
                content="确认要在下次重启后彻底删除该插件及依赖吗？"
                theme="danger"
                placement="top-right"
                @confirm="handleAction(item.id, 'delete')"
              >
                <t-button size="small" theme="danger" variant="text" :disabled="actionLoading">
                  <template #icon><delete-icon /></template>
                </t-button>
              </t-popconfirm>

            </div>
          </div>
        </div>
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
.line-clamp-2 {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
}
:deep(.t-tag) {
  font-weight: 600;
}
.design-card {
  transition: all 0.3s ease;
}
</style>
