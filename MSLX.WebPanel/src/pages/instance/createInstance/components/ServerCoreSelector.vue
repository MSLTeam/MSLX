<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  getServerCoreClassify,
  getServerCoreGameVersion,
  getServerCoreDownloadInfo,
  getServerCoreBuilds,
} from '@/api/mslapi/serverCore';
import type { ServerCoreClassifyModel } from '@/api/mslapi/model/serverCore';

const props = defineProps<{
  visible: boolean;
}>();

const emit = defineEmits(['update:visible', 'confirm']);

const isVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val),
});

// --- 状态数据 ---
const loadingCategories = ref(false);
const loadingVersions = ref(false);
const categoryData = ref<Partial<ServerCoreClassifyModel>>({});

const currentCategoryKey = ref('plugins');
const selectedCore = ref('');
const versionList = ref<string[]>([]);
const searchText = ref('');

// --- 构建版本状态数据 ---
const buildDialogVisible = ref(false);
const loadingBuilds = ref(false);
const buildList = ref<string[]>([]);
const selectedVersionForBuild = ref('');

// 分类映射
const categoryConfig = [
  {
    key: 'plugins',
    name: '插件服务端',
    desc: '支持 Bukkit/Spigot/Paper 插件',
    icon: 'app',
    dataKey: 'pluginsCore' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'forge_hybrid',
    name: 'NeoForge 系混合服务端',
    desc: '同时支持 Neoforge/Forge模组 和 插件',
    icon: 'layers',
    dataKey: 'pluginsAndModsCore_Forge' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'fabric_hybrid',
    name: 'Fabric 混合服务端',
    desc: '同时支持 Fabric模组 和 插件',
    icon: 'cpu',
    dataKey: 'pluginsAndModsCore_Fabric' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'mod_forge',
    name: 'NeoForge 模组服务端',
    desc: '纯 NeoForge/Forge 模组支持',
    icon: 'tools',
    dataKey: 'modsCore_Forge' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'mod_fabric',
    name: 'Fabric 模组服务端',
    desc: '纯 Fabric 模组支持',
    icon: 'ai-tool',
    dataKey: 'modsCore_Fabric' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'vanilla',
    name: '原版服务端',
    desc: 'Minecraft 官方原版核心',
    icon: 'tea',
    dataKey: 'vanillaCore' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'bedrock',
    name: '基岩版第三方端',
    desc: '第三方的基岩版服务端',
    icon: 'gift',
    dataKey: 'bedrockCore' as keyof ServerCoreClassifyModel,
  },
  {
    key: 'proxy',
    name: '代理服务端',
    desc: 'BungeeCord / Velocity 等 用于群组服',
    icon: 'share',
    dataKey: 'proxyCore' as keyof ServerCoreClassifyModel,
  },
];

// 获取当前选中分类下的所有核心列表
const currentCoreList = computed(() => {
  const activeCat = categoryConfig.find((c) => c.key === currentCategoryKey.value);
  if (!activeCat || !categoryData.value) return [];
  return categoryData.value[activeCat.dataKey] || [];
});

// 过滤搜索结果
const filteredCores = computed(() => {
  if (!searchText.value) return currentCoreList.value;
  return currentCoreList.value.filter((core) => core.toLowerCase().includes(searchText.value.toLowerCase()));
});

// 加载分类数据
const fetchCategories = async () => {
  loadingCategories.value = true;
  try {
    const res = await getServerCoreClassify();
    if (Array.isArray(res) && res.length > 0) {
      categoryData.value = res[0];
    } else if (res && !Array.isArray(res)) {
      categoryData.value = res as any;
    }
  } catch (error) {
    MessagePlugin.error('获取服务端分类失败');
    console.error(error);
  } finally {
    loadingCategories.value = false;
  }
};

// 切换分类
const handleCategoryChange = (key: string) => {
  currentCategoryKey.value = key;
  selectedCore.value = '';
  versionList.value = [];
  searchText.value = '';
};

// 选中核心 -> 获取版本
const handleCoreSelect = (core: string) => {
  if (selectedCore.value === core) return;
  selectedCore.value = core;
  fetchVersions(core);
};

const fetchVersions = async (coreName: string) => {
  loadingVersions.value = true;
  versionList.value = [];
  try {
    const res = await getServerCoreGameVersion(coreName);
    versionList.value = res.versions || [];
  } catch (error) {
    MessagePlugin.error(`获取 ${coreName} 版本列表失败`);
    console.error(error);
  } finally {
    loadingVersions.value = false;
  }
};

// 选中版本 -> 拦截判断是否需要选构建版本
const handleVersionSelect = async (version: string) => {
  if (selectedCore.value === 'forge' || selectedCore.value === 'neoforge') {
    selectedVersionForBuild.value = version;
    await fetchBuilds(selectedCore.value, version);
  } else {
    // 普通的
    await fetchDownloadInfo(selectedCore.value, version, 'latest');
  }
};

// 获取构建版本列表
const fetchBuilds = async (coreName: string, version: string) => {
  loadingBuilds.value = true;
  buildList.value = [];
  buildDialogVisible.value = true;

  try {
    const res: any = await getServerCoreBuilds(coreName, version);
    const builds = res || [];
    if (builds && builds.length > 0) {
      buildList.value = builds;
    } else {
      buildList.value = ['latest'];
    }
  } catch (error) {
    MessagePlugin.error(`获取 ${coreName} 构建版本失败`);
    console.error(error);
    buildList.value = ['latest']; // 兜底
  } finally {
    loadingBuilds.value = false;
  }
};

// 获取下载链接并返回给父组件
const fetchDownloadInfo = async (core: string, version: string, build: string) => {
  if (core === 'bedrock-server') {
    MessagePlugin.warning('不支持在此部署基岩版官方版服务端，请使用基岩版一键部署/更新功能！');
    return;
  }
  const loadingInstance = MessagePlugin.loading('正在获取核心下载信息...', 0);

  try {
    const res: any = await getServerCoreDownloadInfo(core, version, build);

    // 构造返回数据
    const result = {
      core: core,
      version: version,
      build: build,
      url: res.url,
      sha256: res.sha256 || '',
      filename: `${core}-${version}.jar`,
    };

    MessagePlugin.close(loadingInstance);
    emit('confirm', result);
    isVisible.value = false;
    buildDialogVisible.value = false;
  } catch (error) {
    MessagePlugin.close(loadingInstance);
    MessagePlugin.error('获取核心的下载信息失败');
    console.error(error);
  }
};

// 初始化
watch(
  () => props.visible,
  (val) => {
    if (val && !categoryData.value.pluginsCore) {
      fetchCategories();
    }
  },
);
</script>

<template>
  <t-dialog
    v-model:visible="isVisible"
    header="选择服务端核心"
    width="90%"
    top="5vh"
    attach="body"
    :footer="false"
    destroy-on-close
    class="core-selector-dialog"
  >
    <div class="flex flex-col md:flex-row h-[75vh] bg-zinc-50 dark:bg-zinc-900/80 overflow-hidden">
      <div
        class="w-full md:w-64 lg:w-72 shrink-0 bg-white/90 dark:bg-zinc-800/90 backdrop-blur-md border-b md:border-b-0 md:border-r border-zinc-200/70 dark:border-zinc-700/60 flex flex-col z-10 shadow-[2px_0_8px_rgba(0,0,0,0.02)]"
      >
        <div class="hidden md:block p-5 pb-3">
          <h3 class="text-base font-extrabold text-[var(--td-text-color-primary)] m-0 tracking-tight">服务端分类</h3>
          <p class="text-xs text-[var(--td-text-color-secondary)] mt-1 font-medium">选择您需要的底层架构类型</p>
        </div>

        <div
          class="flex-1 overflow-x-auto md:overflow-y-auto custom-scrollbar flex flex-row md:flex-col gap-2 p-3 md:p-4 hide-scrollbar-on-mobile"
        >
          <t-loading
            v-if="loadingCategories"
            :loading="loadingCategories"
            size="small"
            text="加载分类中..."
            class="m-auto"
          />

          <template v-else>
            <div
              v-for="cat in categoryConfig"
              :key="cat.key"
              class="group flex items-center md:items-start gap-3 p-2.5 md:p-3 rounded-xl cursor-pointer border border-transparent transition-all duration-300 shrink-0 md:shrink"
              :class="
                currentCategoryKey === cat.key
                  ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/20 shadow-sm'
                  : 'hover:bg-zinc-100 dark:hover:bg-zinc-700/50 hover:border-zinc-200 dark:hover:border-zinc-600'
              "
              @click="handleCategoryChange(cat.key)"
            >
              <div
                class="w-10 h-10 rounded-lg flex items-center justify-center shrink-0 transition-colors"
                :class="
                  currentCategoryKey === cat.key
                    ? 'bg-[var(--color-primary)] text-white shadow-md shadow-[var(--color-primary)]/30'
                    : 'bg-zinc-100 dark:bg-zinc-800 text-[var(--td-text-color-secondary)] group-hover:text-zinc-700 dark:group-hover:text-zinc-200'
                "
              >
                <t-icon :name="cat.icon" size="20px" />
              </div>

              <div class="flex flex-col min-w-0 pr-2 md:pr-0">
                <div
                  class="font-bold text-sm truncate transition-colors"
                  :class="
                    currentCategoryKey === cat.key ? 'text-[var(--color-primary)]' : 'text-zinc-700 dark:text-zinc-300'
                  "
                >
                  {{ cat.name }}
                </div>
                <div class="hidden md:block text-[11px] text-[var(--td-text-color-secondary)] leading-snug mt-0.5">
                  {{ cat.desc }}
                </div>
                <div
                  v-if="categoryData[cat.dataKey]"
                  class="hidden md:inline-flex items-center mt-1.5 w-max px-1.5 py-0.5 rounded bg-zinc-200/50 dark:bg-zinc-700/50 text-[var(--td-text-color-secondary)] text-[10px] font-mono font-bold"
                >
                  {{ categoryData[cat.dataKey]?.length || 0 }} CORES
                </div>
              </div>
            </div>
          </template>
        </div>
      </div>

      <div class="flex-1 flex flex-col min-w-0 relative overflow-y-auto md:overflow-hidden">
        <div
          class="flex-1 flex flex-col min-h-[240px] shrink-0 md:min-h-0 md:shrink p-4 sm:p-6 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 bg-white/40 dark:bg-zinc-900/40"
        >
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-4 shrink-0">
            <div class="flex items-center gap-2">
              <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
              <h3 class="text-sm font-bold text-[var(--td-text-color-primary)] m-0">选择服务端核心</h3>
            </div>
            <t-input
              v-model="searchText"
              placeholder="搜索核心名称..."
              class="!w-full sm:!w-64 !bg-white dark:!bg-zinc-800"
            >
              <template #prefix-icon><t-icon name="search" class="opacity-60" /></template>
            </t-input>
          </div>

          <div class="flex-1 overflow-y-auto custom-scrollbar pr-2 pb-2">
            <div
              v-if="filteredCores.length > 0"
              class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3"
            >
              <div
                v-for="coreName in filteredCores"
                :key="coreName"
                class="group relative flex items-center justify-center p-4 rounded-xl border bg-white dark:bg-zinc-800 cursor-pointer transition-all duration-300 select-none overflow-hidden"
                :class="
                  selectedCore === coreName
                    ? 'border-[var(--color-primary)] shadow-md shadow-[var(--color-primary)]/20'
                    : 'border-zinc-200 dark:border-zinc-700 hover:border-[var(--color-primary)]/50 hover:shadow-sm'
                "
                @click="handleCoreSelect(coreName)"
              >
                <div
                  class="absolute inset-0 bg-[var(--color-primary)] transition-transform duration-300 origin-bottom"
                  :class="selectedCore === coreName ? 'scale-y-100 opacity-100' : 'scale-y-0 opacity-0'"
                ></div>

                <span
                  class="relative z-10 font-extrabold text-sm truncate transition-colors duration-300"
                  :class="
                    selectedCore === coreName
                      ? 'text-white'
                      : 'text-zinc-700 dark:text-zinc-300 group-hover:text-[var(--color-primary)]'
                  "
                >
                  {{ coreName }}
                </span>

                <t-icon
                  v-if="selectedCore === coreName"
                  name="check"
                  class="absolute top-2 right-2 text-white/80 text-sm z-10"
                />
              </div>
            </div>

            <div v-else class="h-full flex flex-col items-center justify-center opacity-60">
              <t-icon name="search" size="32px" class="text-zinc-400 mb-2" />
              <span class="text-sm text-zinc-500 font-medium">该分类下暂无匹配的核心</span>
            </div>
          </div>
        </div>

        <div
          class="flex-1 flex flex-col min-h-[240px] shrink-0 md:min-h-0 md:shrink p-4 sm:p-6 bg-zinc-50/50 dark:bg-zinc-800/30"
        >
          <div class="flex items-center justify-between mb-4 shrink-0">
            <div class="flex items-center gap-2">
              <div class="w-1 h-4 bg-emerald-500 rounded-full"></div>
              <h3 class="text-sm font-bold text-[var(--td-text-color-primary)] m-0">
                <span v-if="selectedCore" class="text-[var(--color-primary)] mr-1">{{ selectedCore }}</span>
                支持版本列表
              </h3>
            </div>

            <t-button
              v-if="selectedCore"
              size="small"
              variant="text"
              class="hover:!bg-zinc-200/50 dark:hover:!bg-zinc-700/50"
              @click="fetchVersions(selectedCore)"
            >
              <template #icon><t-icon name="refresh" /></template>刷新版本
            </t-button>
          </div>

          <div class="flex-1 overflow-y-auto custom-scrollbar pr-2 pb-2 relative">
            <t-loading
              v-if="loadingVersions"
              :loading="loadingVersions"
              size="small"
              text="获取版本中..."
              class="absolute inset-0 m-auto"
            />

            <template v-else>
              <div v-if="!selectedCore" class="h-full flex items-center justify-center">
                <span
                  class="text-sm font-medium text-[var(--td-text-color-secondary)] bg-white dark:bg-zinc-800 px-4 py-2 rounded-full shadow-sm border border-[var(--td-component-border)]"
                  >请先在上方选择一个核心</span
                >
              </div>

              <div
                v-else-if="versionList.length > 0"
                class="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-6 xl:grid-cols-8 gap-2"
              >
                <div
                  v-for="ver in versionList"
                  :key="ver"
                  class="flex items-center justify-center px-2 py-1.5 rounded-lg border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 text-xs font-mono font-bold text-zinc-600 dark:text-zinc-300 cursor-pointer shadow-sm transition-all hover:bg-[var(--color-primary)]/10 hover:border-[var(--color-primary)]/40 hover:text-[var(--color-primary)] hover:-translate-y-0.5 active:translate-y-0"
                  @click="handleVersionSelect(ver)"
                >
                  {{ ver }}
                </div>
              </div>

              <div v-else class="h-full flex items-center justify-center opacity-60">
                <span class="text-sm text-zinc-500 font-medium">未找到该核心的版本信息</span>
              </div>
            </template>
          </div>
        </div>
      </div>
    </div>

    <t-dialog
      v-model:visible="buildDialogVisible"
      :header="`${selectedCore} ${selectedVersionForBuild} 选择构建版本`"
      width="500px"
      top="15vh"
      attach="body"
      :footer="false"
      :z-index="10270"
    >
      <div class="min-h-[150px] max-h-[50vh] overflow-y-auto custom-scrollbar p-2">
        <t-loading
          v-if="loadingBuilds"
          :loading="loadingBuilds"
          size="small"
          text="获取构建版本中..."
          class="flex justify-center mt-10"
        />

        <template v-else>
          <div v-if="buildList.length > 0" class="flex flex-col gap-2">
            <div
              v-for="(build, index) in buildList"
              :key="build"
              class="flex items-center justify-between px-4 py-3 rounded-lg border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800/40 cursor-pointer transition-all hover:border-[var(--color-primary)] hover:shadow-sm hover:bg-[var(--color-primary)]/5 group"
              @click="fetchDownloadInfo(selectedCore, selectedVersionForBuild, build)"
            >
              <div class="flex items-center gap-3">
                <t-icon name="server" class="text-zinc-400 group-hover:text-[var(--color-primary)] transition-colors" />
                <span
                  class="font-mono text-sm font-bold text-zinc-700 dark:text-zinc-300 group-hover:text-[var(--color-primary)] transition-colors"
                >
                  {{ build }}
                </span>
              </div>

              <div class="flex items-center">
                <span
                  v-if="index === 0 && build !== 'latest'"
                  class="text-[11px] text-emerald-600 dark:text-emerald-400 font-bold bg-emerald-100 dark:bg-emerald-900/40 px-2 py-0.5 rounded-full border border-emerald-200 dark:border-emerald-800/50"
                >
                  推荐/最新
                </span>
                <t-icon
                  name="chevron-right"
                  class="ml-2 text-zinc-300 group-hover:text-[var(--color-primary)] transition-colors"
                />
              </div>
            </div>
          </div>

          <div v-else class="flex flex-col items-center justify-center opacity-60 mt-8">
            <t-icon name="error-circle" size="24px" class="text-zinc-400 mb-2" />
            <span class="text-sm text-zinc-500">未获取到构建版本</span>
          </div>
        </template>
      </div>
    </t-dialog>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar.less';
@reference "@/style/tailwind/index.css";

:deep(.t-dialog__body) {
  @apply !p-0 !overflow-hidden;
}

:deep(.t-dialog__header) {
  @apply !pb-4 border-b border-zinc-100 dark:border-zinc-800;
}

/* 滚动条混入 */
.custom-scrollbar {
  .scrollbar-mixin();
}

/* 移动端隐藏横向滚动条以保持美观 */
.hide-scrollbar-on-mobile::-webkit-scrollbar {
  @media (max-width: 768px) {
    display: none;
  }
}
.hide-scrollbar-on-mobile {
  @media (max-width: 768px) {
    scrollbar-width: none;
    -ms-overflow-style: none;
  }
}
</style>
