<script lang="ts" setup>
import { onMounted, reactive, ref } from 'vue';
import {
  AddIcon,
  CheckCircleFilledIcon,
  CloseCircleFilledIcon,
  CpuIcon,
  DeleteIcon,
  LoadingIcon,
  MinusCircleFilledIcon,
  RefreshIcon,
  FilterIcon,
  UsergroupIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import { useInstanceListStore } from '@/store/modules/instance';
import type { InstanceListModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { postDeleteInstance, postInstanceAction } from '@/api/instance';

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

// ================= 批量操作状态管理 =================
const isBatchMode = ref(false);
const selectedIds = ref<number[]>([]);
const batchLoading = ref(false);

const toggleBatchMode = () => {
  isBatchMode.value = !isBatchMode.value;
  selectedIds.value = []; // 每次切换模式重置选中状态
};

// 实例卡片点击事件
const handleCardClick = (item: InstanceListModel) => {
  if (isBatchMode.value) {
    // 选中卡片
    const index = selectedIds.value.indexOf(item.id);
    if (index === -1) {
      selectedIds.value.push(item.id);
    } else {
      selectedIds.value.splice(index, 1);
    }
  } else {
    changeUrl(`/instance/console/${item.id}`);
  }
};

// 批量执行操作
const handleBatchAction = (action: 'start' | 'stop' | 'restart' | 'delete') => {
  if (selectedIds.value.length === 0) {
    MessagePlugin.warning('请先选择要操作的实例');
    return;
  }

  const actionMap = {
    start: '启动',
    stop: '停止',
    restart: '重启',
    delete: '删除',
  };
  const actionName = actionMap[action];
  const isDelete = action === 'delete';

  const confirmDialog = DialogPlugin.confirm({
    header: `确认批量${actionName}`,
    body: `您确定要对已选中的 ${selectedIds.value.length} 个实例执行${actionName}操作吗？${
      isDelete ? '（注意：删除操作不可逆，批量删除默认不清理磁盘上的服务端数据文件）' : ''
    }`,
    theme: isDelete ? 'danger' : ('primary' as any),
    onConfirm: async () => {
      confirmDialog.hide();
      batchLoading.value = true;
      const msg = MessagePlugin.loading(`正在批量${actionName}中，请稍候...`);

      try {
        const promises = selectedIds.value.map((id) => {
          if (isDelete) {
            return postDeleteInstance(id, false);
          } else {
            return postInstanceAction(id, action);
          }
        });

        const results = await Promise.allSettled(promises);

        const rejected = results.filter((p) => p.status === 'rejected');
        if (rejected.length > 0) {
          MessagePlugin.warning({
            content: `操作完成，但有 ${rejected.length} 个实例执行${actionName}失败`,
            duration: 5000,
          });
        } else {
          MessagePlugin.success(`批量${actionName}操作成功`);
        }

        // 刷新列表并清中状态
        selectedIds.value = [];
        isBatchMode.value = false;
        await store.refreshInstanceList();
      } catch (e: any) {
        MessagePlugin.error(`批量操作出现异常: ${e.message}`);
      } finally {
        MessagePlugin.close(msg);
        batchLoading.value = false;
      }
    },
  });
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

// 单独删除状态
const deleteState = reactive({
  visible: false,
  loading: false,
  deleteFile: false,
  item: null as InstanceListModel | null,
});

const handleDelete = (e: MouseEvent, item: InstanceListModel) => {
  e.stopPropagation();
  deleteState.item = item;
  deleteState.deleteFile = false;
  deleteState.loading = false;
  deleteState.visible = true;
};

const handleConfirmDelete = async () => {
  if (!deleteState.item) return;
  deleteState.loading = true;
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
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col sm:flex-row flex-wrap sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start shrink-0 flex-1 min-w-0">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">服务端列表</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">
          管理您的 Minecraft 服务器实例，监控运行状态与核心版本
        </p>
      </div>

      <div class="flex flex-col sm:flex-row flex-wrap items-center sm:justify-end gap-3">
        <template v-if="!isBatchMode">
          <t-button variant="outline" :disabled="!store.instanceList?.length" @click="toggleBatchMode">
            <template #icon><filter-icon /></template>
            批量操作
          </t-button>
          <t-button variant="dashed" @click="store.refreshInstanceList">
            <template #icon><refresh-icon /></template>
            刷新列表
          </t-button>
          <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/instance/create')">
            <template #icon><add-icon /></template>
            添加服务端
          </t-button>
        </template>

        <template v-else>
          <div class="flex items-center bg-zinc-100 dark:bg-zinc-800/80 rounded-lg p-1">
            <span class="px-3 text-sm font-medium text-[var(--td-text-color-secondary)]">
              已选 <span class="text-[var(--color-primary)] font-bold">{{ selectedIds.length }}</span> 项
            </span>
            <div class="flex items-center border-l border-[var(--td-component-border)] pl-1 ml-1 gap-1">
              <t-button
                size="small"
                theme="primary"
                variant="text"
                :disabled="!selectedIds.length || batchLoading"
                @click="handleBatchAction('start')"
                >启动</t-button
              >
              <t-button
                size="small"
                theme="warning"
                variant="text"
                :disabled="!selectedIds.length || batchLoading"
                @click="handleBatchAction('restart')"
                >重启</t-button
              >
              <t-button
                size="small"
                theme="danger"
                variant="text"
                :disabled="!selectedIds.length || batchLoading"
                @click="handleBatchAction('stop')"
                >停止</t-button
              >
              <t-button
                v-if="userStore.isAdmin"
                size="small"
                theme="danger"
                variant="text"
                :disabled="!selectedIds.length || batchLoading"
                @click="handleBatchAction('delete')"
                >删除</t-button
              >
            </div>
          </div>
          <t-button variant="outline" :disabled="batchLoading" @click="toggleBatchMode">取消批量</t-button>
        </template>
      </div>
    </div>

    <div v-loading="false" class="relative min-h-[400px]">
      <template v-if="store.instanceList && store.instanceList.length > 0">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
          <div
            v-for="(item, index) in store.instanceList"
            :key="item.id"
            class="list-item-anim h-full"
            :style="{ animationDelay: `${index * 0.05}s` }"
          >
            <div
              class="design-card relative h-full group flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-4 cursor-pointer"
              :class="{
                '!border-[var(--color-primary)] !bg-[var(--color-primary)]/5 shadow-md':
                  isBatchMode && selectedIds.includes(item.id),
              }"
              @click="handleCardClick(item)"
            >
              <div v-if="isBatchMode" class="absolute top-4 right-4 z-10 pointer-events-none">
                <t-checkbox :checked="selectedIds.includes(item.id)" />
              </div>

              <div class="flex items-center gap-4">
                <div class="relative shrink-0">
                  <t-avatar
                    :image="getImageUrl(item.icon, item.id)"
                    class="shadow-sm border border-[var(--td-component-border)] !bg-[var(--td-bg-color-secondarycontainer)] !rounded-xl"
                    shape="round"
                    size="56px"
                  />
                  <span class="absolute -bottom-0.5 -right-0.5 flex h-3.5 w-3.5">
                    <span
                      v-if="item.status === 2"
                      class="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"
                    ></span>
                    <span
                      :class="item.status === 2 ? 'bg-emerald-500' : 'bg-zinc-300 dark:bg-zinc-600'"
                      class="relative inline-flex rounded-full h-3.5 w-3.5 border-2 border-white dark:border-zinc-800"
                    ></span>
                  </span>
                </div>

                <div class="flex-1 min-w-0 pr-4">
                  <div class="flex items-center min-w-0">
                    <h4 class="flex-1 text-base font-bold text-[var(--td-text-color-primary)] truncate tracking-tight">
                      {{ item.name }}
                    </h4>
                    <span class="text-xs font-mono text-[var(--td-text-color-secondary)] ml-2 opacity-70 shrink-0"
                      >#{{ item.id }}</span
                    >
                  </div>

                  <div class="mt-2 flex items-center gap-4 w-full">
                    <div class="flex-1 min-w-0 flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]">
                      <cpu-icon class="opacity-80 shrink-0" size="14px" />
                      <span class="truncate font-medium">{{ formatCore(item.core) }}</span>
                    </div>

                    <div
                      v-if="item.extra && item.extra.onlinePlayers > 0 && item.status === 2"
                      class="flex items-center gap-1 text-xs text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-900/30 px-1.5 py-0.5 rounded-md shrink-0 whitespace-nowrap"
                    >
                      <usergroup-icon size="14px" />
                      <span class="font-bold">{{ item.extra.onlinePlayers }}</span>
                    </div>

                    <div
                      :class="
                        getStatusConfig(item.status).theme === 'success'
                          ? 'text-emerald-600 dark:text-emerald-400'
                          : 'text-[var(--td-text-color-secondary)]'
                      "
                      class="text-xs font-bold shrink-0 whitespace-nowrap"
                    >
                      {{ getStatusConfig(item.status).label }}
                    </div>
                  </div>
                </div>
              </div>

              <div
                class="flex items-center justify-between pt-3 mt-auto border-t border-dashed border-zinc-200 dark:border-zinc-700/60"
              >
                <span
                  class="text-xs text-[var(--td-text-color-secondary)] group-hover:text-[var(--color-primary)] transition-colors font-semibold"
                >
                  {{ isBatchMode ? (selectedIds.includes(item.id) ? '点击取消选择' : '点击选择实例') : '控制台 →' }}
                </span>

                <div class="flex items-center gap-1" v-if="!isBatchMode">
                  <t-button
                    v-if="userStore.isAdmin"
                    class="hover:!bg-red-500/10"
                    shape="circle"
                    size="small"
                    theme="danger"
                    variant="text"
                    @click.stop="(e) => handleDelete(e, item)"
                  >
                    <template #icon><delete-icon size="32" /></template>
                  </t-button>
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
        <t-empty class="!bg-transparent" description="暂无服务端实例" />
      </div>
    </div>

    <t-dialog
      v-model:visible="deleteState.visible"
      :confirm-btn="{ content: '确认删除', theme: 'danger', loading: deleteState.loading }"
      cancel-btn="取消"
      header="确认删除服务端"
      @confirm="handleConfirmDelete"
    >
      <div class="delete-dialog-body">
        <div class="alert-zinc bg-red-500/5 border border-red-500/20 p-4 rounded-xl mb-4">
          <p class="text-[var(--td-text-color-primary)] font-bold mb-1">
            您确定要删除 <span class="text-red-500">{{ deleteState.item?.name }}</span> 吗？
          </p>
          <p class="text-xs text-red-500/80 italic">此操作不可撤销，服务端配置与运行记录将被抹除。</p>
        </div>

        <div class="px-1">
          <t-checkbox v-model="deleteState.deleteFile">
            <span class="text-[var(--td-text-color-secondary)] text-sm">同时清理磁盘上的服务端数据文件</span>
          </t-checkbox>
        </div>
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

/* 深度适配 TDesign */
:deep(.t-avatar) {
  @apply ring-1 ring-zinc-200/50 dark:ring-zinc-700/50;
}
</style>
