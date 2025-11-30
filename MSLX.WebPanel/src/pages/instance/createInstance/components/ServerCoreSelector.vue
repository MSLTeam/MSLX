<template>
  <t-dialog
    v-model:visible="isVisible"
    header="选择服务端核心"
    width="900px"
    top="5vh"
    :footer="false"
    destroy-on-close
    class="core-selector-dialog"
  >
    <div class="core-selector-layout">
      <div class="sidebar">
        <div class="sidebar-title">服务端分类</div>
        <div class="sidebar-desc">选择您需要的服务端类型</div>

        <t-loading v-if="loadingCategories" :loading="loadingCategories" size="small" text="加载分类中..." />

        <div v-else class="category-list">
          <div
            v-for="cat in categoryConfig"
            :key="cat.key"
            class="category-item"
            :class="{ active: currentCategoryKey === cat.key }"
            @click="handleCategoryChange(cat.key)"
          >
            <div class="cat-icon">
              <t-icon :name="cat.icon" />
            </div>
            <div class="cat-info">
              <div class="cat-name">{{ cat.name }}</div>
              <div class="cat-desc">{{ cat.desc }}</div>
              <div v-if="categoryData[cat.dataKey]" class="cat-count">
                {{ categoryData[cat.dataKey]?.length || 0 }} 个核心
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="main-panel">
        <div class="panel-section">
          <div class="section-header">
            <div class="section-title">选择服务端核心</div>
            <t-input v-model="searchText" placeholder="搜索核心..." class="search-input">
              <template #suffix-icon><t-icon name="search" /></template>
            </t-input>
          </div>

          <div class="core-grid">
            <div
              v-for="coreName in filteredCores"
              :key="coreName"
              class="core-card"
              :class="{ active: selectedCore === coreName }"
              @click="handleCoreSelect(coreName)"
            >
              {{ coreName }}
              <t-icon v-if="selectedCore === coreName" name="check" class="check-icon" />
            </div>
            <div v-if="filteredCores.length === 0" class="empty-text">该分类下暂无核心</div>
          </div>
        </div>

        <div v-if="selectedCore" class="panel-section">
          <t-divider />
          <div class="section-header">
            <div class="section-title">{{ selectedCore }} 版本列表</div>
            <t-button size="small" variant="text" @click="fetchVersions(selectedCore)">
              <template #icon><t-icon name="refresh" /></template>刷新
            </t-button>
          </div>

          <t-loading :loading="loadingVersions" size="small" text="获取版本中..." class="loading-wrapper">
            <div class="version-grid">
              <div
                v-for="ver in versionList"
                :key="ver"
                class="version-item"
                @click="handleVersionSelect(ver)"
              >
                {{ ver }}
              </div>
              <div v-if="versionList.length === 0 && !loadingVersions" class="empty-text">
                未找到该核心的版本信息
              </div>
            </div>
          </t-loading>
        </div>
      </div>
    </div>
  </t-dialog>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  getServerCoreClassify,
  getServerCoreGameVersion,
  getServerCoreDownloadInfo
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

// 分类映射
const categoryConfig = [
  {
    key: 'plugins',
    name: '插件服务端',
    desc: '支持 Bukkit/Spigot/Paper 插件',
    icon: 'app',
    dataKey: 'pluginsCore' as keyof ServerCoreClassifyModel
  },
  {
    key: 'forge_hybrid',
    name: 'NeoForge 系混合服务端',
    desc: '同时支持 Neoforge/Forge模组 和 插件',
    icon: 'layers',
    dataKey: 'pluginsAndModsCore_Forge' as keyof ServerCoreClassifyModel
  },
  {
    key: 'fabric_hybrid',
    name: 'Fabric 混合服务端',
    desc: '同时支持 Fabric模组 和 插件',
    icon: 'cpu',
    dataKey: 'pluginsAndModsCore_Fabric' as keyof ServerCoreClassifyModel
  },
  {
    key: 'vanilla',
    name: '原版服务端',
    desc: 'Mojang 官方原版核心',
    icon: 'logo-github',
    dataKey: 'vanillaCore' as keyof ServerCoreClassifyModel
  },
  {
    key: 'mod_forge',
    name: 'NeoForge 模组服务端',
    desc: '纯 NeoForge/Forge 模组支持',
    icon: 'tools',
    dataKey: 'modsCore_Forge' as keyof ServerCoreClassifyModel
  },
  {
    key: 'proxy',
    name: '代理服务端',
    desc: 'BungeeCord / Velocity 等',
    icon: 'share',
    dataKey: 'proxyCore' as keyof ServerCoreClassifyModel
  },
];

// 获取当前选中分类下的所有核心列表
const currentCoreList = computed(() => {
  const activeCat = categoryConfig.find(c => c.key === currentCategoryKey.value);
  if (!activeCat || !categoryData.value) return [];
  return categoryData.value[activeCat.dataKey] || [];
});

// 过滤搜索结果
const filteredCores = computed(() => {
  if (!searchText.value) return currentCoreList.value;
  return currentCoreList.value.filter(core =>
    core.toLowerCase().includes(searchText.value.toLowerCase())
  );
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
    if (Array.isArray(res) && res.length > 0) {
      versionList.value = res[0].versionList || [];
    } else if(res && !Array.isArray(res)) {
      versionList.value = (res as any).versionList || [];
    }
  } catch (error) {
    MessagePlugin.error(`获取 ${coreName} 版本列表失败`);
    console.error(error);
  } finally {
    loadingVersions.value = false;
  }
};

// 选中版本 -> 获取下载链接并返回给父组件
const handleVersionSelect = async (version: string) => {
  const loadingInstance = MessagePlugin.loading('正在获取核心版本信息...', 0);

  try {
    const res = await getServerCoreDownloadInfo(selectedCore.value, version);

    // 构造返回数据
    const result = {
      core: selectedCore.value,
      version: version,
      url: res.url,
      sha256: res.sha256 || '',
      filename: `${selectedCore.value}-${version}.jar`
    };

    MessagePlugin.close(loadingInstance);

    emit('confirm', result);
    isVisible.value = false; // 关闭弹窗
  } catch (error) {
    MessagePlugin.close(loadingInstance);

    MessagePlugin.error('获取核心的版本信息失败');
    console.error(error);
  }
};

// 初始化
watch(() => props.visible, (val) => {
  if (val && !categoryData.value.pluginsCore) {
    fetchCategories();
  }
});
</script>

<style scoped lang="less">
.core-selector-layout {
  display: flex;
  height: 550px; // 固定高度，内部滚动
  border-top: 1px solid var(--td-border-level-2-color);
}

// 左侧侧边栏
.sidebar {
  width: 240px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-right: 1px solid var(--td-border-level-2-color);
  display: flex;
  flex-direction: column;
  padding: 16px;
  flex-shrink: 0;

  &-title {
    font-size: 18px;
    font-weight: 600;
    color: var(--td-text-color-primary);
    margin-bottom: 4px;
  }

  &-desc {
    font-size: 12px;
    color: var(--td-text-color-secondary);
    margin-bottom: 16px;
  }
}

.category-list {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.category-item {
  display: flex;
  align-items: flex-start;
  padding: 12px;
  border-radius: var(--td-radius-medium);
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.2s;
  background-color: var(--td-bg-color-container);

  &:hover {
    background-color: var(--td-bg-color-component-hover);
  }

  &.active {
    background-color: var(--td-brand-color-light);
    border-color: var(--td-brand-color);

    .cat-name { color: var(--td-brand-color); }
    .cat-icon { color: var(--td-brand-color); }
  }

  .cat-icon {
    font-size: 20px;
    margin-right: 12px;
    margin-top: 2px;
    color: var(--td-text-color-secondary);
  }

  .cat-info {
    flex: 1;
    .cat-name {
      font-weight: 500;
      font-size: 14px;
      margin-bottom: 2px;
    }
    .cat-desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      line-height: 1.4;
    }
    .cat-count {
      margin-top: 4px;
      font-size: 10px;
      color: var(--td-brand-color);
      background: var(--td-bg-color-component);
      display: inline-block;
      padding: 0 4px;
      border-radius: 4px;
    }
  }
}

// 右侧主面板
.main-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0 24px;
  overflow-y: hidden; // 让内部滚动
}

.panel-section {
  display: flex;
  flex-direction: column;
  padding: 16px 0;

  &:first-child {
    flex: 1; // 核心列表占多一点空间
    min-height: 0;
  }
  &:last-child {
    flex: 1; // 版本列表也占空间
    min-height: 0;
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-shrink: 0;

  .section-title {
    font-size: 16px;
    font-weight: 600;
  }

  .search-input {
    width: 200px;
  }
}

// 核心网格
.core-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
  overflow-y: auto;
  padding-bottom: 8px;
  padding-right: 4px; // 防止滚动条遮挡

  .core-card {
    border: 1px solid var(--td-border-level-2-color);
    border-radius: var(--td-radius-medium);
    padding: 16px;
    text-align: center;
    cursor: pointer;
    font-weight: 500;
    transition: all 0.2s;
    position: relative;
    user-select: none;

    &:hover {
      border-color: var(--td-brand-color-focus);
      color: var(--td-brand-color);
    }

    &.active {
      background-color: var(--td-brand-color);
      color: #fff;
      border-color: var(--td-brand-color);

      .check-icon {
        display: block;
      }
    }

    .check-icon {
      display: none;
      position: absolute;
      top: 4px;
      right: 4px;
      font-size: 14px;
    }
  }
}

// 版本网格
.loading-wrapper {
  flex: 1;
  min-height: 0;
  overflow: hidden;
  display: flex;
}

.version-grid {
  flex: 1;
  overflow-y: auto;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
  grid-auto-rows: max-content;
  gap: 8px;
  padding-right: 4px;

  .version-item {
    border: 1px dashed var(--td-border-level-2-color);
    border-radius: var(--td-radius-small);
    padding: 8px;
    text-align: center;
    font-size: 13px;
    cursor: pointer;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 4px;
    transition: all 0.2s;

    &:hover {
      border-color: var(--td-brand-color);
      color: var(--td-brand-color);
      background-color: var(--td-bg-color-container-hover);
    }
  }
}

.empty-text {
  color: var(--td-text-color-placeholder);
  text-align: center;
  padding: 24px;
  width: 100%;
}

@media (max-width: 768px) {
  :deep(.t-dialog) {
    width: 95vw !important;
    max-width: 95vw !important;
  }

  // 垂直堆叠
  .core-selector-layout {
    flex-direction: column;
    height: 70vh;
  }

  // 侧边栏改为顶部横向或短列表
  .sidebar {
    width: 100%;
    height: 160px; // 固定一个较小的高度
    border-right: none;
    border-bottom: 1px solid var(--td-border-level-2-color);
    padding: 12px;

    // 隐藏描述文字
    .cat-desc {
      display: none;
    }

    // 缩小标题
    .sidebar-title {
      font-size: 16px;
    }
    .sidebar-desc {
      display: none;
    }
  }

  .category-item {
    padding: 8px 12px;
    .cat-icon {
      font-size: 18px;
    }
    .cat-info .cat-name {
      font-size: 13px;
    }
  }

  .main-panel {
    width: 100%;
    padding: 12px;
  }

  .section-header {
    flex-direction: column; // 垂直排列标题和搜索/按钮
    align-items: flex-start;
    gap: 8px;

    .search-input {
      width: 100%; // 搜索框占满
    }
    .t-button {
      align-self: flex-end; // 按钮靠右
    }
  }

  // 网格更加紧凑
  .core-grid {
    grid-template-columns: repeat(auto-fill, minmax(100px, 1fr)); // 允许更小的卡片
    gap: 8px;

    .core-card {
      padding: 10px;
      font-size: 13px;
    }
  }

  .version-grid {
    grid-template-columns: repeat(auto-fill, minmax(80px, 1fr));
  }
}
</style>
