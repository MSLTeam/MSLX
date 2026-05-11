<script setup lang="ts">
import { ref } from 'vue';
import { BookFilledIcon, AppIcon, StoreIcon, RefreshIcon, SearchIcon } from 'tdesign-icons-vue-next';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';

import InstalledPlugins from './components/InstalledPlugins.vue';
import PluginMarket from './components/PluginMarket.vue';

const activeTab = ref('installed');
const searchKeyword = ref('');

const installedRef = ref<InstanceType<typeof InstalledPlugins> | null>(null);
const marketRef = ref<InstanceType<typeof PluginMarket> | null>(null);

const triggerRefresh = () => {
  if (installedRef.value) installedRef.value.getList();
};

const triggerSearch = () => {
  if (marketRef.value) marketRef.value.handleSearch(searchKeyword.value);
};
</script>

<template>
  <div class="mx-auto flex flex-col gap-5 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col xl:flex-row xl:items-center justify-between gap-5 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left transition-all"
    >
      <div class="flex flex-col gap-1.5 items-start">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">插件管理</h2>
        <div class="leading-relaxed text-sm text-[var(--td-text-color-secondary)] m-0">
          <span v-if="activeTab === 'installed'"
            >本地插件存放于
            <code
              class="bg-blue-100/60 dark:bg-blue-800/60 px-1.5 py-0.5 rounded text-xs mx-1 font-mono font-bold text-blue-600 dark:text-blue-300"
              >DaemonData/Plugins</code
            >
            目录，启动时自动加载。</span
          >
          <span v-else>浏览官方插件市场，发现更多功能，一键下载至本地 (需要重启生效哦) 。</span>
        </div>
      </div>

      <div class="flex items-center gap-3 flex-wrap">
        <t-radio-group
          v-model="activeTab"
          variant="default-filled"
          class="!bg-zinc-100 dark:!bg-zinc-800/60 !p-1 rounded-xl"
        >
          <t-radio-button value="installed" class="!rounded-lg">
            <div class="flex items-center gap-1 font-medium"><app-icon />已安装</div>
          </t-radio-button>
          <t-radio-button value="market" class="!rounded-lg">
            <div class="flex items-center gap-1 font-medium"><store-icon />插件市场</div>
          </t-radio-button>
        </t-radio-group>

        <div class="w-[1px] h-6 bg-zinc-200 dark:bg-zinc-700/60 mx-1 hidden sm:block"></div>

        <t-button v-if="activeTab === 'installed'" variant="dashed" class="!rounded-xl" @click="triggerRefresh">
          <template #icon><refresh-icon /></template>
          刷新列表
        </t-button>

        <div v-if="activeTab === 'market'" class="w-full sm:w-auto">
          <t-input
            v-model="searchKeyword"
            placeholder="搜索插件..."
            clearable
            class="!w-full sm:!w-64"
            @enter="triggerSearch"
            @clear="triggerSearch"
          >
            <template #prefixIcon><search-icon /></template>
          </t-input>
        </div>

        <t-button theme="primary" class="!rounded-xl" @click="changeUrl(DOC_URLS.plugin_dev)">
          <template #icon><book-filled-icon /></template>
          开发指北
        </t-button>
      </div>
    </div>

    <transition name="fade" mode="out-in">
      <keep-alive>
        <installed-plugins v-if="activeTab === 'installed'" ref="installedRef" @go-market="activeTab = 'market'" />
        <plugin-market v-else-if="activeTab === 'market'" ref="marketRef" />
      </keep-alive>
    </transition>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
.fade-enter-active,
.fade-leave-active {
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateY(10px);
}
:deep(.t-radio-button) {
  border: none !important;
}
:deep(.t-radio-button.t-is-checked) {
  background-color: var(--td-bg-color-container) !important;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}
</style>
