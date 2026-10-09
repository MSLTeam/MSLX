<script lang="ts" setup>
import { ref, reactive, onMounted, onBeforeUnmount, computed, watch } from 'vue';
import { SearchIcon } from 'tdesign-icons-vue-next';
import { searchResources, getResourceVersions, getResourceDetail } from '@/api/resourceCenter';
import type { ResourceModel, ResourceVersionModel } from '@/api/model/resourceCenter';
import { getServerCoreGameVersion } from '@/api/mslapi/serverCore';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useSettingStore, useNodeStore } from '@/store';
import DependencyGuideModal from './components/DependencyGuideModal.vue';
import { useInstanceListStore } from '@/store/modules/instance';
import NodeSwitcher from '@/components/node-switcher/index.vue';

const typeOptions = [
  { label: 'Mod', value: 0 },
  { label: '资源包', value: 1 },
  { label: '数据包', value: 2 },
  { label: '光影', value: 3 },
  { label: '整合包', value: 4 },
  { label: '插件', value: 5 },
];

const loaderOptions = computed(() => {
  if (filter.type === 5) {
    return [
      { label: '全部加载器', value: '' },
      { label: 'Bukkit', value: 'Bukkit' },
      { label: 'Paper', value: 'Paper' },
      { label: 'Spigot', value: 'Spigot' },
      { label: 'Purpur', value: 'Purpur' },
      { label: 'Sponge', value: 'Sponge' },
      { label: 'BungeeCord', value: 'BungeeCord' },
      { label: 'Velocity', value: 'Velocity' },
    ];
  } else if (filter.type === 0) {
    return [
      { label: '全部加载器', value: '' },
      { label: 'Forge', value: 'Forge' },
      { label: 'Fabric', value: 'Fabric' },
      { label: 'NeoForge', value: 'NeoForge' },
      { label: 'Quilt', value: 'Quilt' },
      { label: 'LiteLoader', value: 'LiteLoader' },
    ];
  } else {
    return [];
  }
});

interface CategoryOption {
  label: string;
  value: string;
  icon?: string;
}

const categoryOptions = computed<CategoryOption[]>(() => {
  switch (filter.type) {
    case 0: // Mod (模组)
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '科技与机械', value: 'technology', icon: 'setting' },
        { label: '魔法探秘', value: 'magic', icon: 'star' },
        { label: '冒险与探索', value: 'adventure', icon: 'explore' },
        { label: '建筑与装饰', value: 'decoration', icon: 'palette' },
        { label: '性能优化', value: 'optimization', icon: 'dashboard' },
        { label: '实用工具', value: 'utility', icon: 'tools' },
        { label: '存储与物品', value: 'storage', icon: 'folder' },
        { label: '农业与饮食', value: 'food', icon: 'apple' },
        { label: '装备与战斗', value: 'equipment', icon: 'user-safety' },
        { label: '游戏机制', value: 'game-mechanics', icon: 'gamepad' },
        { label: '世界生成', value: 'worldgen', icon: 'map' },
        { label: '交通与载具', value: 'transportation', icon: 'rocket' },
        { label: '社交与联机', value: 'social', icon: 'chat' },
        { label: '恶搞娱乐', value: 'cursed', icon: 'heart' },
      ];
    case 5: // 插件
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '管理与运维', value: 'management', icon: 'control-platform' },
        { label: '经济与商业', value: 'economy', icon: 'cart' },
        { label: '实用工具', value: 'utility', icon: 'tools' },
        { label: '社交与聊天', value: 'social', icon: 'chat' },
        { label: '游戏机制', value: 'game-mechanics', icon: 'gamepad' },
        { label: '小游戏', value: 'minigame', icon: 'play-circle' },
        { label: '性能优化', value: 'optimization', icon: 'dashboard' },
        { label: '冒险与RPG', value: 'adventure', icon: 'explore' },
        { label: '世界与地图', value: 'worldgen', icon: 'map' },
        { label: '物品与存储', value: 'storage', icon: 'folder' },
        { label: '传送与交通', value: 'transportation', icon: 'rocket' },
        { label: '装备与战斗', value: 'equipment', icon: 'user-safety' },
        { label: '魔法系统', value: 'magic', icon: 'star' },
        { label: '科技机制', value: 'technology', icon: 'layers' },
        { label: '建筑装饰', value: 'decoration', icon: 'palette' },
        { label: '恶搞娱乐', value: 'cursed', icon: 'heart' },
      ];
    case 1: // 资源包
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '原版风格', value: 'vanilla-like', icon: 'brush' },
        { label: '写实逼真', value: 'photorealistic', icon: 'image' },
        { label: '中世纪', value: 'medieval', icon: 'city' },
        { label: '现代风格', value: 'modern', icon: 'city' },
        { label: '蒸汽朋克', value: 'steampunk', icon: 'setting' },
        { label: '16x 分辨率', value: '16x', icon: 'format-vertical-align-center' },
        { label: '32x 分辨率', value: '32x', icon: 'format-vertical-align-center' },
        { label: '64x 分辨率', value: '64x', icon: 'format-vertical-align-center' },
        { label: '128x 分辨率', value: '128x', icon: 'format-vertical-align-center' },
        { label: '256x+ 分辨率', value: '256x+', icon: 'format-vertical-align-center' },
      ];
    case 2: // 数据包
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '冒险与探索', value: 'adventure', icon: 'explore' },
        { label: '游戏机制', value: 'game-mechanics', icon: 'gamepad' },
        { label: '实用工具', value: 'utility', icon: 'tools' },
        { label: '魔法探秘', value: 'magic', icon: 'star' },
        { label: '科技机械', value: 'technology', icon: 'setting' },
        { label: '世界生成', value: 'worldgen', icon: 'map' },
        { label: '建筑与装饰', value: 'decoration', icon: 'palette' },
        { label: '性能优化', value: 'optimization', icon: 'dashboard' },
      ];
    case 3: // 光影
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '性能优化 / 低配', value: 'performance', icon: 'dashboard' },
        { label: '逼真写实', value: 'realistic', icon: 'browse' },
        { label: '奇幻梦幻', value: 'fantasy', icon: 'star' },
        { label: '原版增强', value: 'vanilla-like', icon: 'brush' },
      ];
    case 4: // 整合包
      return [
        { label: '全部分类', value: '', icon: 'app' },
        { label: '冒险与探索', value: 'adventure', icon: 'explore' },
        { label: '任务引导', value: 'quest', icon: 'bulletpoint' },
        { label: '科技机械', value: 'technology', icon: 'setting' },
        { label: '魔法探秘', value: 'magic', icon: 'star' },
        { label: '硬核生存', value: 'challenging', icon: 'error-circle' },
        { label: '多人联机', value: 'multiplayer', icon: 'usergroup' },
        { label: '性能优化', value: 'optimization', icon: 'dashboard' },
        { label: '综合杂锦', value: 'kitchen-sink', icon: 'view-module' },
      ];
    default:
      return [];
  }
});

const selectCategory = (categoryVal: string) => {
  if (filter.category === categoryVal) return;
  filter.category = categoryVal;
  pagination.current = 1;
  handleSearch();
};

const providerOptions = [
  { label: '全部来源', value: -1 },
  { label: 'Modrinth', value: 0 },
  { label: 'CurseForge', value: 1 },
];

const versionOptions = ref<{ label: string; value: string }[]>([]);
const selectedLoader = ref('');

const filter = reactive({
  query: '',
  type: 0,
  provider: -1,
  gameVersion: '',
  category: '',
  offset: 0,
  limit: 24,
  useMirror: localStorage.getItem('mslx_use_mirror') !== 'false',
});

watch(
  () => filter.useMirror,
  (val) => {
    localStorage.setItem('mslx_use_mirror', String(val));
    handleSearch();
  }
);

const pagination = reactive({
  current: 1,
  pageSize: 24,
  total: 1000, // 模糊搜索总数
});

const resourceList = ref<ResourceModel[]>([]);
const loading = ref(false);
let searchRequestId = 0; // 用于丢弃过期响应，避免竞态条件
let currentAbortController: AbortController | null = null; // 用于打断/取消进行中的网络请求

const loadVanillaVersions = async () => {
  try {
    const res = await getServerCoreGameVersion('vanilla');
    if (res && res.versions) {
      versionOptions.value = [
        { label: '全部版本', value: '' },
        ...res.versions.map((v: string) => ({ label: v, value: v })),
      ];
    }
  } catch (error) {
    console.error('Failed to load vanilla versions', error);
  }
};

const handleTypeChange = () => {
  selectedLoader.value = '';
  filter.category = '';
  pagination.current = 1;
  handleSearch();
};

const handleSearch = async () => {
  // 1. 如果上一个请求正在进行，立即主动打断取消，防止并发串调与无用带宽浪费
  if (currentAbortController) {
    currentAbortController.abort();
    currentAbortController = null;
  }

  const abortController = new AbortController();
  currentAbortController = abortController;

  loading.value = true;
  filter.offset = (pagination.current - 1) * pagination.pageSize;
  filter.limit = pagination.pageSize;
  const currentRequestId = ++searchRequestId; // 标记本次请求序号

  const searchPayload = {
    ...filter,
    provider: filter.provider === -1 ? undefined : filter.provider,
    gameLoaders: selectedLoader.value ? [selectedLoader.value] : [],
    pluginLoaders: selectedLoader.value ? [selectedLoader.value] : [],
  };

  try {
    const res = await searchResources(searchPayload, abortController.signal);
    if (currentRequestId !== searchRequestId) return; // 已有更新的请求，丢弃本次结果
    if (res && res.items) {
      resourceList.value = res.items;
      pagination.total = res.totalCount;
    } else if (Array.isArray(res)) {
      // Fallback in case backend is still returning array
      resourceList.value = res;
      pagination.total = res.length;
    } else {
      resourceList.value = [];
      pagination.total = 0;
    }
  } catch (error: any) {
    // 若请求被主动中止/打断，不作为错误处理
    if (error?.name === 'CanceledError' || error?.code === 'ERR_CANCELED' || abortController.signal.aborted) {
      return;
    }
    console.error('Search failed', error);
  } finally {
    if (currentRequestId === searchRequestId) {
      loading.value = false;
      currentAbortController = null;
    }
  }
};

const handlePageChange = (pageInfo: any) => {
  pagination.current = pageInfo.current;
  pagination.pageSize = pageInfo.pageSize;
  handleSearch();
};

const formatNumber = (num: number) => {
  if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
  if (num >= 10000) return (num / 10000).toFixed(1) + 'W';
  if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
  return num.toString();
};

const nodeStore = useNodeStore();
const instanceStore = useInstanceListStore();
const selectedInstanceId = ref<number | null>(null);

const handleNodeChange = (_val?: string) => {
  selectedInstanceId.value = null;
  instanceStore.refreshInstanceList();
};

watch(
  () => nodeStore.activeNodeId,
  () => {
    selectedInstanceId.value = null;
    instanceStore.refreshInstanceList();
  },
);

onMounted(() => {
  loadVanillaVersions();
  handleSearch();
  instanceStore.refreshInstanceList();
});

onBeforeUnmount(() => {
  if (currentAbortController) {
    currentAbortController.abort();
    currentAbortController = null;
  }
});

// 版本下载
const downloadVisible = ref(false);
const versionLoading = ref(false);
const allVersionList = ref<ResourceVersionModel[]>([]);
const currentItem = ref<ResourceModel | null>(null);

const modalFilter = reactive({
  gameVersion: '',
  loader: '',
  environment: -1,
});

const modalEnvironmentOptions = [
  { label: '全部端', value: -1 },
  { label: '客户端包', value: 0 },
  { label: '服务端包', value: 1 },
];

const modalVersionOptions = ref<{ label: string; value: string }[]>([]);
const modalLoaderOptions = ref<{ label: string; value: string }[]>([]);

const versionPagination = reactive({
  current: 1,
  pageSize: 10,
});

const filteredVersionList = computed(() => {
  let list = allVersionList.value;
  if (modalFilter.gameVersion) {
    list = list.filter((v) => v.gameVersions?.includes(modalFilter.gameVersion));
  }
  if (modalFilter.loader) {
    list = list.filter((v) => v.loaders?.includes(modalFilter.loader));
  }
  if (modalFilter.environment !== -1) {
    list = list.filter((v) => (v.environment || 0) === modalFilter.environment);
  }
  return list;
});

const paginatedVersionList = computed(() => {
  const start = (versionPagination.current - 1) * versionPagination.pageSize;
  return filteredVersionList.value.slice(start, start + versionPagination.pageSize);
});

const handleModalFilterChange = () => {
  versionPagination.current = 1;
};

const versionColumns = [
  { colKey: 'name', title: '文件名', ellipsis: true },
  { colKey: 'versionNumber', title: '版本号' },
  { colKey: 'op', title: '操作', width: 100 },
];

const fetchAllVersionsForModal = async () => {
  if (!currentItem.value) return;
  versionLoading.value = true;
  allVersionList.value = [];
  try {
    const versions = await getResourceVersions(currentItem.value.provider, currentItem.value.id, '', '', filter.useMirror);
    allVersionList.value = versions || [];

    const gvs = new Set<string>();
    const lds = new Set<string>();
    allVersionList.value.forEach((v) => {
      v.gameVersions?.forEach((g) => gvs.add(g));
      v.loaders?.forEach((l) => lds.add(l));
    });

    const sortedGvs = Array.from(gvs).sort((a, b) => b.localeCompare(a, undefined, { numeric: true }));

    modalVersionOptions.value = [{ label: '全部版本', value: '' }, ...sortedGvs.map((v) => ({ label: v, value: v }))];
    modalLoaderOptions.value = [
      { label: '全部加载器', value: '' },
      ...Array.from(lds).map((l) => ({ label: l, value: l })),
    ];
  } catch (error) {
    console.error('Failed to fetch versions', error);
  } finally {
    versionLoading.value = false;
  }
};

const openDownloadModal = async (item: ResourceModel) => {
  currentItem.value = item;
  downloadVisible.value = true;

  modalFilter.gameVersion = filter.gameVersion || '';
  modalFilter.loader = selectedLoader.value || '';
  modalFilter.environment = -1;
  versionPagination.current = 1;

  await fetchAllVersionsForModal();
};

const dependencyVisible = ref(false);
const currentVersion = ref<ResourceVersionModel | null>(null);

const doDownload = (row: ResourceVersionModel) => {
  currentVersion.value = row;
  dependencyVisible.value = true;
};

// 详情 Modal
const detailVisible = ref(false);
const detailLoading = ref(false);
const currentDetail = ref<ResourceModel | null>(null);

const openDetailModal = async (item: ResourceModel) => {
  detailVisible.value = true;
  detailLoading.value = true;
  currentDetail.value = null;
  try {
    const res = await getResourceDetail(item.provider, item.id, filter.useMirror);
    if (res) {
      currentDetail.value = res;
    }
  } catch (error) {
    console.error('Failed to fetch resource details', error);
  } finally {
    detailLoading.value = false;
  }
};

// Markdown 主题跟随系统
const settingStore = useSettingStore();
const isDark = computed(() => settingStore.displayMode === 'dark');
const mdTheme = ref(isDark.value ? 'dark' : 'light');
watch(isDark, (val) => {
  mdTheme.value = val ? 'dark' : 'light';
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start shrink-0 min-w-0">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">资源中心</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">搜索并下载服务端插件、Mod 和其他资源包</p>
        <div class="flex items-center gap-2 mt-1">
          <t-switch v-model="filter.useMirror" size="small" />
          <span class="text-xs text-[var(--td-text-color-secondary)]">
            使用 <a href="https://www.mcimirror.top/" target="_blank" class="text-[var(--td-brand-color)] hover:underline">MCIM 镜像源</a> 获取中文翻译和加速
          </span>
        </div>
      </div>
      <div class="flex flex-wrap items-center sm:justify-end gap-3">
        <node-switcher @change="handleNodeChange" />
        <t-select
          v-if="filter.type === 0 || filter.type === 5"
          v-model="selectedInstanceId"
          :options="instanceStore.instanceList"
          :keys="{ label: 'name', value: 'id' }"
          placeholder="直装到目标实例 (可选)"
          clearable
          filterable
          style="width: 200px"
        />
        <t-select
          v-model="filter.provider"
          :options="providerOptions"
          placeholder="全部来源"
          style="width: 120px"
          @change="handleSearch"
        />
        <t-select
          v-model="filter.type"
          :options="typeOptions"
          placeholder="资源类型"
          style="width: 120px"
          @change="handleTypeChange"
        />
        <t-select
          v-model="filter.gameVersion"
          :options="versionOptions"
          placeholder="全部版本"
          clearable
          filterable
          style="width: 140px"
          @change="handleSearch"
        />
        <t-select
          v-if="loaderOptions.length > 0"
          v-model="selectedLoader"
          :options="loaderOptions"
          placeholder="全部加载器"
          clearable
          style="width: 120px"
          @change="handleSearch"
        />
        <t-input v-model="filter.query" placeholder="搜索资源..." clearable style="width: 200px" @enter="handleSearch">
          <template #suffixIcon>
            <search-icon style="cursor: pointer" @click="handleSearch" />
          </template>
        </t-input>
        <t-button theme="primary" @click="handleSearch">搜索</t-button>
      </div>
    </div>

    <!-- 主体区域：左侧分类列表（桌面端）/ 顶部水平滑动胶囊栏（移动端） + 右侧资源卡片区 -->
    <div class="flex flex-col lg:flex-row gap-5 items-start">
      <!-- 分类侧栏 / 水平滑动栏 -->
      <aside
        v-if="categoryOptions.length > 0"
        class="w-full lg:w-56 shrink-0 design-card p-3 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm lg:sticky lg:top-4 text-left"
      >
        <div class="text-xs font-bold text-[var(--td-text-color-secondary)] uppercase tracking-wider mb-2.5 hidden lg:block px-2">
          分类筛选
        </div>
        <!-- 桌面端垂直列表 -->
        <div class="flex flex-row lg:flex-col gap-1.5 overflow-x-auto lg:overflow-y-auto custom-scrollbar max-h-none lg:max-h-[calc(100vh-220px)] pb-1 lg:pb-0">
          <button
            v-for="cat in categoryOptions"
            :key="cat.value"
            type="button"
            class="px-3 py-2 rounded-xl text-xs sm:text-sm font-medium transition-all duration-200 text-left whitespace-nowrap flex items-center justify-between group cursor-pointer shrink-0 lg:shrink"
            :class="
              filter.category === cat.value
                ? 'bg-[var(--td-brand-color)]/10 text-[var(--td-brand-color)] font-bold border border-[var(--td-brand-color)]/25 shadow-sm'
                : 'text-[var(--td-text-color-secondary)] hover:bg-[var(--td-bg-color-secondarycontainer)] hover:text-[var(--td-text-color-primary)] border border-transparent'
            "
            @click="selectCategory(cat.value)"
          >
            <span class="flex items-center gap-2.5">
              <t-icon v-if="cat.icon" :name="cat.icon" class="text-base shrink-0" />
              <span>{{ cat.label }}</span>
            </span>
          </button>
        </div>
      </aside>

      <!-- 资源卡片网格与分页 -->
      <main class="flex-1 min-w-0 w-full" v-loading="loading">
        <div class="relative min-h-[400px]">
          <template v-if="resourceList && resourceList.length > 0">
            <div class="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
              <div
                v-for="(item, index) in resourceList"
                :key="item.id"
                class="list-item-anim h-full"
                :style="{ animationDelay: `${index * 0.05}s` }"
              >
                <div
                  class="design-card relative h-full flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-4"
                >
                  <div class="flex items-center gap-4">
                    <div class="relative shrink-0">
                      <t-avatar
                        :image="item.iconUrl"
                        class="shadow-sm border border-[var(--td-component-border)] !bg-[var(--td-bg-color-secondarycontainer)] !rounded-xl"
                        shape="round"
                        size="56px"
                      >
                        <template #icon>
                          <span class="text-[var(--td-text-color-secondary)]">{{ item.name.charAt(0) }}</span>
                        </template>
                      </t-avatar>
                    </div>
                    <div class="flex-1 min-w-0 pr-4">
                      <div class="flex items-center min-w-0">
                        <h4 class="flex-1 text-base font-bold text-[var(--td-text-color-primary)] truncate tracking-tight">
                          {{ item.name }}
                        </h4>
                        <t-tag
                          v-if="item.provider === 0"
                          theme="success"
                          variant="light-outline"
                          size="small"
                          class="ml-2 shrink-0"
                          >Modrinth</t-tag
                        >
                        <t-tag
                          v-else-if="item.provider === 1"
                          theme="warning"
                          variant="light-outline"
                          size="small"
                          class="ml-2 shrink-0"
                          >CurseForge</t-tag
                        >
                      </div>
                      <div class="mt-1 flex items-center text-xs text-[var(--td-text-color-secondary)]">
                        <span class="truncate">{{ item.author || 'Unknown' }}</span>
                      </div>
                    </div>
                  </div>
                  <p
                    class="text-sm text-[var(--td-text-color-secondary)] flex-1 overflow-hidden"
                    style="display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 3"
                  >
                    {{ item.summary }}
                  </p>
                  <div class="flex justify-between items-center mt-2 border-t border-[var(--td-component-border)] pt-4">
                    <div class="text-xs text-[var(--td-text-color-secondary)]">
                      下载量: {{ formatNumber(item.downloadCount) }}
                    </div>
                    <div class="flex gap-2">
                      <t-button size="small" theme="default" @click="openDetailModal(item)">详情</t-button>
                      <t-button size="small" theme="primary" @click="openDownloadModal(item)">下载</t-button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </template>

          <div v-if="resourceList.length === 0 && !loading" class="text-center text-[var(--td-text-color-secondary)] my-16">
            未找到相关资源，请修改筛选条件后重试。
          </div>

          <div class="mt-6 flex justify-end">
            <t-pagination
              v-model="pagination.current"
              v-model:page-size="pagination.pageSize"
              :total="pagination.total"
              :page-size-options="[12, 24, 48]"
              @change="handlePageChange"
            />
          </div>
        </div>
      </main>
    </div>

    <!-- 版本选择下载弹窗 -->
    <t-dialog
      v-model:visible="downloadVisible"
      :header="'下载: ' + (currentItem?.name || '')"
      width="800px"
      :footer="false"
    >
      <div class="mb-4 flex gap-4 flex-wrap">
        <t-select
          v-if="filter.type === 0 || filter.type === 5"
          v-model="selectedInstanceId"
          :options="instanceStore.instanceList"
          :keys="{ label: 'name', value: 'id' }"
          placeholder="直装到目标实例 (可选)"
          clearable
          filterable
          style="width: 200px"
        />
        <t-select
          v-model="modalFilter.gameVersion"
          :options="modalVersionOptions"
          placeholder="游戏版本"
          clearable
          filterable
          style="width: 200px"
          @change="handleModalFilterChange"
        />
        <t-select
          v-model="modalFilter.loader"
          :options="modalLoaderOptions"
          placeholder="加载器"
          clearable
          style="width: 200px"
          @change="handleModalFilterChange"
        />
        <t-select
          v-if="filter.type === 4"
          v-model="modalFilter.environment"
          :options="modalEnvironmentOptions"
          placeholder="端类型"
          style="width: 150px"
          @change="handleModalFilterChange"
        />
      </div>
      <t-table
        :data="paginatedVersionList"
        :columns="versionColumns"
        row-key="id"
        :loading="versionLoading"
        hover
        :max-height="400"
      >
        <template #name="{ row }">
          <div class="flex items-center gap-2">
            <span>{{ row.name }}</span>
            <t-tag v-if="row.environment === 1" theme="warning" size="small" variant="light">服务端包</t-tag>
          </div>
        </template>
        <template #op="{ row }">
          <t-button size="small" theme="primary" @click="doDownload(row)">下载</t-button>
        </template>
      </t-table>
      <div class="mt-4 flex justify-end">
        <t-pagination
          v-model="versionPagination.current"
          v-model:page-size="versionPagination.pageSize"
          :total="filteredVersionList.length"
          :page-size-options="[10, 20, 50]"
        />
      </div>
    </t-dialog>

    <!-- 详情弹窗 -->
    <t-dialog
      v-model:visible="detailVisible"
      header="资源详情"
      width="800px"
      :footer="false"
      placement="center"
    >
      <div class="flex flex-col gap-4">
        <div v-if="detailLoading" class="flex justify-center items-center h-48">
          <t-loading text="加载详情中..." />
        </div>
        <div v-else-if="currentDetail">
          <div class="flex items-center gap-4 mb-4">
            <t-avatar
              :image="currentDetail.iconUrl"
              class="shadow-sm border border-[var(--td-component-border)] !bg-[var(--td-bg-color-secondarycontainer)] !rounded-xl"
              shape="round"
              size="64px"
            >
              <template #icon>
                <span class="text-[var(--td-text-color-secondary)]">{{ currentDetail.name.charAt(0) }}</span>
              </template>
            </t-avatar>
            <div>
              <h3 class="text-xl font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">
                {{ currentDetail.name }}
              </h3>
              <p class="text-sm text-[var(--td-text-color-secondary)] mt-1 mb-0">{{ currentDetail.summary }}</p>
            </div>
          </div>

          <!-- Markdown 渲染 -->
          <div class="border-t border-[var(--td-component-border)] pt-4 max-h-[60vh] overflow-y-auto custom-scrollbar">
            <md-preview
              v-if="currentDetail.description"
              :model-value="currentDetail.description"
              :theme="mdTheme as Themes"
              class="custom-md-preview bg-transparent text-left !p-0"
            />
            <div v-else class="text-[var(--td-text-color-secondary)] text-center my-8">
              该资源暂无详细描述。
            </div>
          </div>
        </div>
        <div v-else class="text-center text-[var(--td-text-color-secondary)] h-32 flex items-center justify-center">
          加载失败
        </div>
      </div>
    </t-dialog>

    <dependency-guide-modal
      v-model:visible="dependencyVisible"
      :version="currentVersion"
      :main-resource="currentItem"
      :instance-id="selectedInstanceId"
      :resource-type="filter.type"
    />
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

.list-item-anim {
  opacity: 0;
  animation: fadeInUp 0.4s ease-out forwards;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* === 自定义滚动条样式 === */
.custom-scrollbar {
  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    @apply bg-zinc-300 dark:bg-zinc-600 rounded-full;
  }
}

/* === Markdown 组件样式 === */

:deep(.custom-md-preview) {
  --md-bk-color: transparent !important;
  --md-color: inherit !important;
  text-align: left !important;
}

:deep(.md-editor-preview a) {
  color: var(--color-primary);
  text-decoration: none;
  &:hover {
    text-decoration: underline;
  }
}

:deep(.md-editor-preview code:not([class*="language-"])) {
  color: var(--color-primary);
  background-color: color-mix(in srgb, var(--color-primary), transparent 90%);
  border-radius: 4px;
  padding: 2px 4px;
}

:deep(.md-editor-preview blockquote){
  background: none;
}

:deep(.md-editor div.default-theme) {
  --md-theme-quote-border: 4px solid var(--color-primary);
}

:deep(.md-editor-preview) {
  --md-color: inherit !important;
}

:deep(.md-editor-preview table tr:nth-child(2n)){
  background-color: transparent;
}

:deep(.md-editor-preview table tr:nth-child(n)){
  background-color: transparent;
}

:deep(.md-editor-preview img) {
  max-width: 100%;
  border-radius: 8px;
  margin: 16px 0;
}
</style>
