<script setup lang="ts">
import { onMounted, reactive } from 'vue';
import {
  DeleteIcon,
  CheckCircleFilledIcon,
  CloseCircleFilledIcon,
  CpuIcon,
  LoadingIcon,
  MinusCircleFilledIcon,
  RefreshIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { useInstanceListStore } from '@/store/modules/instance';
import type { InstanceListModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { postDeleteInstance } from '@/api/instance';

// 导入logo资源
import neoforgedImg from '@/assets/serverLogos/neoforged.png';
import forgeImg from '@/assets/serverLogos/150px-Anvil.png';
import customImg from '@/assets/serverLogos/150px-MinecartWithCommandBlock.png';
import defaultImg from '@/assets/serverLogos/150px-Allium.png';
import { BASE_URL_NAME, TOKEN_NAME } from '@/config/global';
import { useUserStore } from '@/store';

const store = useInstanceListStore();
const userStore = useUserStore();

onMounted(() => {
  store.refreshInstanceList();
});

const getStatusConfig = (status: number) => {
  switch (status) {
    case 1: // 启动中
      return { label: '启动中', theme: 'primary', icon: LoadingIcon, loading: true };
    case 2: // 运行中
      return { label: '运行中', theme: 'success', icon: CheckCircleFilledIcon, loading: false };
    case 3: // 停止中
      return { label: '停止中', theme: 'warning', icon: MinusCircleFilledIcon, loading: false };
    case 4: // 重启中
      return { label: '重启中', theme: 'primary', icon: RefreshIcon, loading: true };
    case 0: // 未启动
    default:
      return { label: '未启动', theme: 'default', icon: CloseCircleFilledIcon, loading: false };
  }
};

const handleCardClick = (item: InstanceListModel) => {
  // 跳转服务器控制台
  changeUrl(`/instance/console/${item.id}`);
};

const getImageUrl = (name: string, id: number) => {
  if (name.includes('http')) return name;
  switch (name) {
    case 'neoforge':
      return neoforgedImg;
    case 'forge':
      return forgeImg;
    case 'custom':
      return customImg;
    case 'server-icon':
      return new URL(
        `${localStorage.getItem(BASE_URL_NAME)}/api/instance/icon/${id}.png?x-user-token=${localStorage.getItem(TOKEN_NAME)}`,
        import.meta.url,
      ).href;
    default:
      return defaultImg;
  }
};

const formatCore = (core: string) => {
  if (core === 'none') {
    return '自定义模式';
  }
  if (core.startsWith('@')) {
    if (core.includes('neoforge')) {
      return 'NeoForge';
    } else {
      return 'Forge';
    }
  } else {
    return core.replace('.jar', '');
  }
};

const deleteState = reactive({
  visible: false,
  loading: false,
  deleteFile: false,
  item: null as InstanceListModel | null,
});

const handleDelete = (e: MouseEvent, item: InstanceListModel) => {
  e.stopPropagation();

  deleteState.item = item;
  deleteState.deleteFile = false; // 默认不勾选，防止误删文件
  deleteState.loading = false;

  deleteState.visible = true;
};

const handleConfirmDelete = async () => {
  if (!deleteState.item) return;

  deleteState.loading = true; // 显示按钮加载圈

  try {
    await postDeleteInstance(deleteState.item.id, deleteState.deleteFile);

    MessagePlugin.success('删除成功');
    deleteState.visible = false;

    await store.refreshInstanceList();
  } catch (e: any) {
    MessagePlugin.error('删除失败: ' + e.message);
  } finally {
    deleteState.loading = false;
  }
};
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-zinc-800 dark:text-zinc-200 pb-5">

    <div class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left">
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-zinc-900 dark:text-zinc-100 m-0">服务端列表</h2>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0">管理您的 Minecraft 服务器实例，监控运行状态与核心版本</p>
      </div>

      <div class="flex items-center gap-3">
        <t-button variant="dashed" @click="store.refreshInstanceList">
          <template #icon><refresh-icon /></template>
          刷新列表
        </t-button>
        <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/instance/create')">
          添加服务端
        </t-button>
      </div>
    </div>

    <div v-loading="false" class="relative min-h-[400px]">
      <template v-if="store.instanceList && store.instanceList.length > 0">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
          <div v-for="item in store.instanceList" :key="item.id"
               class="design-card group flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-4 cursor-pointer"
               @click="handleCardClick(item)">

            <div class="flex items-center gap-4">
              <div class="relative shrink-0">
                <t-avatar :image="getImageUrl(item.icon, item.id)" size="56px" shape="round"
                          class="shadow-sm border border-zinc-200/50 dark:border-zinc-700/50 !bg-zinc-100 dark:!bg-zinc-700 !rounded-xl" />
                <span class="absolute -bottom-0.5 -right-0.5 flex h-3.5 w-3.5">
          <span v-if="item.status === 2" class="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
          <span :class="item.status === 2 ? 'bg-emerald-500' : 'bg-zinc-300 dark:bg-zinc-600'"
                class="relative inline-flex rounded-full h-3.5 w-3.5 border-2 border-white dark:border-zinc-800"></span>
        </span>
              </div>

              <div class="flex-1 min-w-0">
                <div class="flex items-center">
                  <h4 class="text-base font-bold text-zinc-900 dark:text-zinc-100 truncate tracking-tight">{{ item.name }}</h4>
                  <span class="text-xs font-mono text-zinc-400 dark:text-zinc-500 ml-2 opacity-70 shrink-0">#{{ item.id }}</span>
                </div>

                <div class="mt-2 flex items-center gap-4">
                  <div class="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400">
                    <cpu-icon size="14px" class="opacity-80" />
                    <span class="truncate font-medium">{{ formatCore(item.core) }}</span>
                  </div>
                  <div :class="getStatusConfig(item.status).theme === 'success' ? 'text-emerald-600 dark:text-emerald-400' : 'text-zinc-600 dark:text-zinc-400'"
                       class="text-xs font-bold">
                    {{ getStatusConfig(item.status).label }}
                  </div>
                </div>
              </div>
            </div>

            <div class="flex items-center justify-between pt-3 mt-auto border-t border-dashed border-zinc-200 dark:border-zinc-700/60">
      <span class="text-xs text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors font-semibold">
        控制台 →
      </span>
              <div class="flex items-center gap-1">
                <t-button v-if="userStore.isAdmin"
                          shape="circle" theme="danger" variant="text"
                          size="small"
                          class="hover:!bg-red-500/10"
                          @click.stop="(e) => handleDelete(e, item)">
                  <template #icon><delete-icon size="32" /></template>
                </t-button>
              </div>
            </div>
          </div>
        </div>
      </template>

      <div v-else class="flex flex-col items-center justify-center py-24 bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-zinc-200/50 dark:border-zinc-700/50">
        <t-empty description="暂无服务端实例" class="!bg-transparent" />
      </div>
    </div>

    <t-dialog
      v-model:visible="deleteState.visible"
      header="确认删除服务端"
      @confirm="handleConfirmDelete"
      :confirm-btn="{ content: '确认删除', theme: 'danger' }"
      cancel-btn="取消"
    >
      <div class="delete-dialog-body">
        <div class="alert-zinc bg-red-500/5 border border-red-500/20 p-4 rounded-xl mb-4">
          <p class="text-zinc-900 dark:text-zinc-100 font-bold mb-1">
            您确定要删除 <span class="text-red-500">{{ deleteState.item?.name }}</span> 吗？
          </p>
          <p class="text-xs text-red-500/80 italic">此操作不可撤销，服务端配置与运行记录将被抹除。</p>
        </div>

        <div class="px-1">
          <t-checkbox v-model="deleteState.deleteFile">
            <span class="text-zinc-600 dark:text-zinc-400 text-sm">同时清理磁盘上的服务端数据文件</span>
          </t-checkbox>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* 列表进场动画 */
.grid > div {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(12px); }
  to { opacity: 1; transform: translateY(0); }
}

/* 动态计算动画延迟 */
.grid > div:nth-child(1) { animation-delay: 0.05s; }
.grid > div:nth-child(2) { animation-delay: 0.1s; }
.grid > div:nth-child(3) { animation-delay: 0.15s; }
.grid > div:nth-child(4) { animation-delay: 0.2s; }

/* 深度适配 TDesign */
:deep(.t-avatar) {
  @apply ring-1 ring-zinc-200/50 dark:ring-zinc-700/50;
}

/* 确保空状态背景不遮挡 */
:deep(.t-empty__description) {
  @apply text-zinc-500 dark:text-zinc-400;
}
</style>
