<script lang="ts" setup>
import { confirmNodeAction } from '@/utils/nodeDialog';
import { getTagTheme } from '@/utils/tagTheme';
import { onMounted, reactive, ref, watch, computed, defineAsyncComponent } from 'vue';
import { useRoute } from 'vue-router';
import {
  AddIcon,
  ArrowLeftIcon,
  CheckCircleFilledIcon,
  CloseCircleFilledIcon,
  CpuIcon,
  DeleteIcon,
  EditIcon,
  GridViewIcon,
  LoadingIcon,
  MinusCircleFilledIcon,
  PlayCircleIcon,
  RefreshIcon,
  FilterIcon,
  SearchIcon,
  StopCircleIcon,
  TagIcon,
  ViewListIcon,
  UsergroupIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { useInstanceListStore } from '@/store/modules/instance';
import type { InstanceListModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { postDeleteInstance, postInstanceAction } from '@/api/instance';

// 导入logo资源
import neoforgedImg from '@/assets/serverLogos/neoforged.png';
import forgeImg from '@/assets/serverLogos/150px-Anvil.png';
import customImg from '@/assets/serverLogos/150px-MinecartWithCommandBlock.png';
import defaultImg from '@/assets/serverLogos/150px-Allium.png';
import { TOKEN_NAME } from '@/config/global';
import { getPageNodeId, getNodeTarget } from '@/utils/nodeTarget';
import { useUserStore, useNodeStore } from '@/store';
import { getMediaToken, getSyncMediaToken } from '@/utils/scopedToken';

const store = useInstanceListStore();
const userStore = useUserStore();
const nodeStore = useNodeStore();

import NodeSwitcher from '@/components/node-switcher/index.vue';
const InstanceSettings = defineAsyncComponent(() => import('../console/components/InstanceSettings.vue'));
const route = useRoute();
const settingsInstanceId = ref<number>();
watch(() => route.fullPath, () => { settingsInstanceId.value = undefined; }, { flush: 'sync' });

const handleNodeChange = (_val: string) => {
  store.refreshInstanceList();
};

watch(
  () => nodeStore.activeNodeId,
  () => {
    selectedIds.value = [];
    isBatchMode.value = false;
    activeTagFilter.value = 'all';
    settingsInstanceId.value = undefined;
    store.refreshInstanceList();
  },
);

onMounted(async () => {
  await getMediaToken();
  store.refreshInstanceList();
});

// 搜索
const searchKeyword = ref('');
const filteredInstanceList = computed(() => {
  if (!store.instanceList) return [];
  if (!searchKeyword.value.trim()) return store.instanceList;

  const keyword = searchKeyword.value.toLowerCase().trim();
  return store.instanceList.filter((item) => {
    return (
      item.name?.toLowerCase().includes(keyword) ||
      String(item.id).includes(keyword) ||
      item.core?.toLowerCase().includes(keyword)
    );
  });
});

const viewModeKey = 'mslx_instance_view_mode';
const viewMode = ref(localStorage.getItem(viewModeKey) === 'category' ? 'category' : 'flat');
const activeTagFilter = ref('all');
const expandedGroupKey = ref('');
watch([activeTagFilter, viewMode, searchKeyword, () => nodeStore.activeNodeId], () => {
  expandedGroupKey.value = '';
});
const toggleViewMode = () => {
  viewMode.value = viewMode.value === 'flat' ? 'category' : 'flat';
  localStorage.setItem(viewModeKey, viewMode.value);
  activeTagFilter.value = 'all';
};

const tagKey = (tag: string) => `tag:${tag}`;
const allTags = computed(() => [...new Set(store.instanceList.flatMap((item) => item.tags ?? []))]);
const hasUntagged = computed(() => store.instanceList.some((item) => !item.tags?.length));
const tagFilters = computed(() => [
  { key: 'all', label: '全部' },
  ...allTags.value.map((tag) => ({ key: tagKey(tag), label: tag })),
  ...(hasUntagged.value ? [{ key: 'untagged', label: '未分类' }] : []),
]);
watch(tagFilters, (filters) => {
  if (!filters.some((filter) => filter.key === activeTagFilter.value)) activeTagFilter.value = 'all';
});

const visibleGroups = computed(() => {
  const list = filteredInstanceList.value;
  if (viewMode.value === 'flat') return list.length ? [{ key: 'all', label: '', items: list }] : [];
  const groups = new Map<string, InstanceListModel[]>();
  for (const item of list) {
    const keys = item.tags?.length ? [...new Set(item.tags)].map(tagKey) : ['untagged'];
    for (const key of keys) {
      const group = groups.get(key);
      if (group) group.push(item);
      else groups.set(key, [item]);
    }
  }
  return tagFilters.value.flatMap(({ key, label }) => {
    const items = groups.get(key);
    return items && (activeTagFilter.value === 'all' || activeTagFilter.value === key) ? [{ key, label, items }] : [];
  });
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
const handleCardClick = (e: Event, item: InstanceListModel, newTab = false) => {
  if (isBatchMode.value) {
    // 选中卡片
    const index = selectedIds.value.indexOf(item.id);
    if (index === -1) {
      selectedIds.value.push(item.id);
    } else {
      selectedIds.value.splice(index, 1);
    }
  } else {
    changeUrl(`/instance/console/${item.id}`, newTab);
  }
};

// 批量执行操作
const handleBatchAction = (
  action: 'start' | 'stop' | 'restart' | 'delete',
  ids = selectedIds.value,
  groupLabel?: string,
) => {
  const targetIds = [...new Set(ids)];
  if (!targetIds.length || batchLoading.value) {
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
  const nodeId = getPageNodeId();
  const scope = groupLabel
    ? `「${groupLabel}」分组${searchKeyword.value.trim() ? '中符合当前搜索的' : '中的'}`
    : '已选中的';

  const confirmDialog = confirmNodeAction({
    header: `确认批量${actionName}`,
    body: `您确定要对${scope} ${targetIds.length} 个实例执行${actionName}操作吗？${
      isDelete ? '（注意：删除操作不可逆，批量删除默认不清理磁盘上的服务端数据文件）' : ''
    }`,
    theme: isDelete ? 'danger' : ('primary' as any),
    onConfirm: async () => {
      confirmDialog.hide();
      batchLoading.value = true;
      const msg = MessagePlugin.loading(`正在批量${actionName}中，请稍候...`);

      try {
        const promises = targetIds.map((id) => {
          if (isDelete) {
            return postDeleteInstance(id, false);
          } else {
            return postInstanceAction(id, action, nodeId);
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
        if (getPageNodeId() === nodeId) {
          selectedIds.value = [];
          isBatchMode.value = false;
          await store.refreshInstanceList();
        }
      } catch (e: any) {
        MessagePlugin.error(`批量操作出现异常: ${e.message}`);
      } finally {
        MessagePlugin.close(msg);
        batchLoading.value = false;
      }
    },
  });
};

const batchGroup = (action: 'start' | 'stop', group: { label: string; items: InstanceListModel[] }) => {
  const status = action === 'start' ? 0 : 2;
  handleBatchAction(action, group.items.filter((item) => item.status === status).map((item) => item.id), group.label);
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
    case 'server-icon': {
      const mediaToken = getSyncMediaToken();
      const fallbackToken = getPageNodeId() === 'local' ? localStorage.getItem(TOKEN_NAME) : '';
      const tokenParam = mediaToken
        ? `?media_token=${mediaToken}`
        : (fallbackToken ? `?x-user-token=${fallbackToken}` : '');
      return new URL(
        `${getNodeTarget().baseUrl}/api/instance/icon/${id}.png${tokenParam}${getPageNodeId() !== 'local' ? `&x-node-id=${encodeURIComponent(getPageNodeId())}` : ''}`,
        import.meta.url,
      ).href;
    }
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
      class="design-card flex flex-col gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
      <div class="flex flex-col gap-1 items-start shrink-0 min-w-0">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">服务端列表</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">
          管理您的 Minecraft 服务器实例，监控运行状态与核心版本
        </p>
      </div>

      <div class="flex flex-wrap items-center sm:justify-end gap-3">
        <template v-if="!isBatchMode">
          <node-switcher @change="handleNodeChange" />
          <t-input
            v-model="searchKeyword"
            placeholder="搜索名称 / ID / 核心..."
            clearable
            class="!w-48 sm:!w-56 shrink-0"
          >
            <template #prefix-icon>
              <search-icon />
            </template>
          </t-input>

          <t-tooltip :content="viewMode === 'flat' ? '切换为分类视图' : '切换为平铺视图'">
            <t-button
              shape="square"
              variant="dashed"
              :aria-label="viewMode === 'flat' ? '切换为分类视图' : '切换为平铺视图'"
              @click="toggleViewMode"
            >
              <template #icon>
                <view-list-icon v-if="viewMode === 'flat'" />
                <grid-view-icon v-else />
              </template>
            </t-button>
          </t-tooltip>

          <t-button variant="outline" :disabled="!store.instanceList?.length" @click="toggleBatchMode">
            <template #icon><filter-icon /></template>
            批量操作
          </t-button>
          <t-button variant="dashed" @click="store.refreshInstanceList">
            <template #icon><refresh-icon /></template>
            刷新列表
          </t-button>
          <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/instance/create')" @auxclick.middle.prevent="changeUrl('/instance/create', true)">
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

      <div
        v-if="viewMode === 'category' && tagFilters.length > 1"
        role="search"
        aria-label="按标签筛选服务端"
        class="flex flex-wrap items-center gap-2 pt-3 border-t border-dashed border-[var(--td-component-border)]"
      >
        <span class="flex shrink-0 items-center gap-1.5 text-xs font-bold tracking-wider text-[var(--td-text-color-secondary)]">
          <tag-icon size="14" class="text-[var(--color-primary)]" />
          分类：
        </span>
        <t-button
          v-for="filter in tagFilters"
          :key="filter.key"
          :aria-pressed="activeTagFilter === filter.key"
          :variant="activeTagFilter === filter.key ? 'base' : 'outline'"
          :theme="activeTagFilter === filter.key ? 'primary' : 'default'"
          shape="round"
          size="small"
          @click="activeTagFilter = filter.key"
        >
          {{ filter.label }}
        </t-button>
      </div>
    </div>

    <div v-loading="false" class="relative min-h-[400px]">
      <div v-if="visibleGroups.length" class="flex flex-col gap-6">
        <section v-for="group in visibleGroups" :key="group.key" class="flex flex-col gap-3.5">
          <div
            v-if="viewMode === 'category'"
            class="design-card flex flex-wrap items-center justify-between gap-3 px-5 py-3 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
          >
            <div class="flex min-w-0 grow basis-24 flex-wrap items-center gap-x-3 gap-y-1">
              <div class="flex min-w-0 items-center gap-2 sm:gap-3">
                <span aria-hidden="true" class="h-4 w-1.5 shrink-0 rounded-full bg-[var(--color-primary)]"></span>
                <h3 class="m-0 min-w-0 break-all text-base font-bold tracking-tight text-[var(--td-text-color-primary)]">
                  {{ group.label }}
                </h3>
              </div>
              <span class="rounded-full px-2.5 py-0.5 text-xs font-medium bg-[var(--td-bg-color-secondarycontainer)] text-[var(--td-text-color-secondary)]">
                {{ group.items.filter((item) => item.status === 2).length }}/{{ group.items.length }} 运行中
              </span>
            </div>
            <div v-if="!isBatchMode" class="ml-auto flex shrink-0 flex-row-reverse items-center gap-1.5">
              <t-button
                size="small"
                shape="square"
                variant="text"
                :aria-label="`${group.label}分组操作`"
                :aria-expanded="expandedGroupKey === group.key"
                :aria-controls="`instance-group-actions-${encodeURIComponent(group.key)}`"
                @click="expandedGroupKey = expandedGroupKey === group.key ? '' : group.key"
              >
                <template #icon><arrow-left-icon /></template>
              </t-button>
              <transition
                enter-active-class="motion-safe:transition motion-safe:duration-150"
                enter-from-class="translate-x-2 opacity-0"
                leave-active-class="motion-safe:transition motion-safe:duration-150"
                leave-to-class="translate-x-2 opacity-0"
              >
                <div
                  v-show="expandedGroupKey === group.key"
                  :id="`instance-group-actions-${encodeURIComponent(group.key)}`"
                  class="flex items-center gap-1.5"
                >
                  <t-button
                    size="small" variant="outline" theme="primary" shape="round"
                    :disabled="batchLoading || !group.items.some((item) => item.status === 0)"
                    @click="batchGroup('start', group)"
                  >
                    <template #icon><play-circle-icon size="14" aria-hidden="true" /></template>
                    全部启动
                  </t-button>
                  <t-button
                    size="small" variant="outline" theme="danger" shape="round"
                    :disabled="batchLoading || !group.items.some((item) => item.status === 2)"
                    @click="batchGroup('stop', group)"
                  >
                    <template #icon><stop-circle-icon size="14" aria-hidden="true" /></template>
                    全部停止
                  </t-button>
                </div>
              </transition>
            </div>
          </div>

        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
          <div
            v-for="(item, index) in group.items"
            :key="item.id"
            class="list-item-anim h-full"
            :style="{ animationDelay: `${index * 0.05}s` }"
          >
            <div
              class="design-card relative h-full group flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-4 cursor-pointer"
              :class="{
                '!border-[var(--color-primary)] shadow-md': isBatchMode && selectedIds.includes(item.id),
              }"
              @click="handleCardClick($event, item)"
              @auxclick.middle.prevent="handleCardClick($event, item, true)"
            >
              <div v-if="isBatchMode" class="absolute top-4 right-4 z-10 pointer-events-none">
                <t-checkbox :checked="selectedIds.includes(item.id)" />
              </div>

              <div class="flex items-center gap-4">
                <div class="relative shrink-0">
                  <t-avatar
                    class="shadow-sm border border-[var(--td-component-border)] !bg-[var(--td-bg-color-secondarycontainer)] !rounded-xl overflow-hidden"
                    shape="round"
                    size="56px"
                  >
                    <img 
                      :src="getImageUrl(item.icon, item.id)" 
                      :fetchpriority="index < 4 ? 'high' : 'low'" 
                      :loading="index < 4 ? 'eager' : 'lazy'" 
                      alt="server icon"
                      class="w-full h-full object-cover"
                    />
                  </t-avatar>
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
                    <t-tag v-if="item.java === 'docker-java' || item.java === 'docker-custom'" theme="primary"
                      >Docker</t-tag
                    >
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

              <div v-if="item.expireTime" class="mt-2 w-full flex">
                <t-tag
                  size="small"
                  variant="light"
                  class="!w-auto max-w-full !rounded-md"
                  :theme="
                    Date.parse(item.expireTime.replace(' ', 'T') + 'Z') <= Date.now()
                      ? 'danger'
                      : Date.parse(item.expireTime.replace(' ', 'T') + 'Z') - Date.now() < 3 * 24 * 60 * 60 * 1000
                        ? 'warning'
                        : 'default'
                  "
                >
                  <template #icon>
                    <t-icon name="time" size="14px" class="mr-1 opacity-80 shrink-0" />
                  </template>
                  <span class="truncate font-mono text-[11px]">
                    {{ Date.parse(item.expireTime.replace(' ', 'T') + 'Z') <= Date.now() ? '已到期' : '到期' }}:
                    {{ item.expireTime }}
                  </span>
                </t-tag>
              </div>

              <div
                class="flex items-center justify-between gap-2 min-h-10 pt-3 mt-auto border-t border-dashed border-zinc-200 dark:border-zinc-700/60"
              >
                <span
                  class="shrink-0 whitespace-nowrap text-xs text-[var(--td-text-color-secondary)] group-hover:text-[var(--color-primary)] transition-colors font-semibold"
                >
                  {{ isBatchMode ? (selectedIds.includes(item.id) ? '点击取消选择' : '点击选择实例') : '控制台 →' }}
                </span>

                <div class="flex min-w-0 items-center gap-2">
                  <t-popup v-if="item.tags?.length" trigger="click" placement="top-right" :disabled="isBatchMode">
                    <button
                      type="button"
                      class="flex min-w-0 items-center gap-1 rounded-md text-left focus-visible:outline-2 focus-visible:outline-primary"
                      :aria-label="`查看 ${item.name} 的标签`"
                      :tabindex="isBatchMode ? -1 : 0"
                      @click="!isBatchMode && $event.stopPropagation()"
                      @auxclick.middle="!isBatchMode && $event.stopPropagation()"
                    >
                      <t-tag
                        v-for="tag in item.tags.slice(0, 2)"
                        :key="tag"
                        size="small"
                        variant="light"
                        :theme="getTagTheme(tag)"
                        :max-width="80"
                        class="min-w-0 max-w-24"
                      >
                        {{ tag }}
                      </t-tag>
                      <t-tag v-if="item.tags.length > 2" size="small" variant="light" class="shrink-0">
                        +{{ item.tags.length - 2 }}
                      </t-tag>
                    </button>
                    <template #content>
                      <ul class="m-0 max-h-48 w-max max-w-[min(16rem,80vw)] overflow-y-auto list-none p-1 text-xs">
                        <li v-for="tag in item.tags" :key="tag" class="break-all py-0.5">{{ tag }}</li>
                      </ul>
                    </template>
                  </t-popup>
                  <div v-if="!isBatchMode && userStore.isAdmin" class="flex shrink-0 items-center gap-1">
                    <t-button
                      size="small"
                      shape="circle"
                      variant="text"
                      :aria-label="`编辑 ${item.name} 的实例设置`"
                      title="实例设置"
                      @click.stop="settingsInstanceId = item.id"
                      @auxclick.middle.stop.prevent
                    >
                      <template #icon><edit-icon /></template>
                    </t-button>
                    <t-button
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
        </div>
        </section>
      </div>

      <div
        v-else
        class="flex flex-col items-center justify-center py-24 bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-[var(--td-component-border)]"
      >
        <t-empty class="!bg-transparent" :description="store.instanceList.length ? '没有符合条件的服务端实例' : '暂无服务端实例'" />
      </div>
    </div>

    <instance-settings
      v-if="settingsInstanceId"
      :key="settingsInstanceId"
      :server-id="settingsInstanceId"
      open-on-mount
      @closed="settingsInstanceId = undefined"
      @saved="store.refreshInstanceList"
    />

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
  content-visibility: auto;
  contain-intrinsic-size: auto 150px;
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
