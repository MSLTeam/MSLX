<script lang="ts" setup>
import { ref, reactive, onMounted } from 'vue';
import { SearchIcon } from 'tdesign-icons-vue-next';
import { searchResources, getResourceVersions, getResourceDetail } from '@/api/resourceCenter';
import type { ResourceModel, ResourceVersionModel } from '@/api/model/resourceCenter';
import { getServerCoreGameVersion } from '@/api/mslapi/serverCore';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useSettingStore } from '@/store';

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
});

const pagination = reactive({
  current: 1,
  pageSize: 24,
  total: 1000, // 模糊搜索总数
});

const resourceList = ref<ResourceModel[]>([]);
const loading = ref(false);

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
  handleSearch();
};

const handleSearch = async () => {
  loading.value = true;
  filter.offset = (pagination.current - 1) * pagination.pageSize;
  filter.limit = pagination.pageSize;

  const searchPayload = {
    ...filter,
    provider: filter.provider === -1 ? undefined : filter.provider,
    gameLoaders: selectedLoader.value ? [selectedLoader.value] : [],
    pluginLoaders: selectedLoader.value ? [selectedLoader.value] : [],
  };

  try {
    const res = await searchResources(searchPayload);
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
  } catch (error) {
    console.error('Search failed', error);
  } finally {
    loading.value = false;
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

onMounted(() => {
  loadVanillaVersions();
  handleSearch();
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

import { computed } from 'vue';

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
    const versions = await getResourceVersions(currentItem.value.provider, currentItem.value.id, '', '');
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

const doDownload = (url: string) => {
  window.open(url, '_blank');
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
    const res = await getResourceDetail(item.provider, item.id);
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
import { watch } from 'vue';
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
      </div>
      <div class="flex flex-wrap items-center sm:justify-end gap-3">
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
        <t-input v-model="filter.query" placeholder="搜索资源..." clearable @enter="handleSearch" style="width: 200px">
          <template #suffixIcon>
            <search-icon @click="handleSearch" style="cursor: pointer" />
          </template>
        </t-input>
        <t-button theme="primary" @click="handleSearch">搜索</t-button>
      </div>
    </div>

    <div v-loading="loading" class="relative min-h-[400px]">
      <template v-if="resourceList && resourceList.length > 0">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
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

    <!-- 版本选择下载弹窗 -->
    <t-dialog
      v-model:visible="downloadVisible"
      :header="'下载: ' + (currentItem?.name || '')"
      width="800px"
      :footer="false"
    >
      <div class="mb-4 flex gap-4">
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
          <t-button size="small" theme="primary" @click="doDownload(row.downloadUrl)">下载</t-button>
        </template>
      </t-table>
      <div class="mt-4 flex justify-end">
        <t-pagination
          v-model="versionPagination.current"
          v-model:pageSize="versionPagination.pageSize"
          :total="filteredVersionList.length"
          :pageSizeOptions="[10, 20, 50]"
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
