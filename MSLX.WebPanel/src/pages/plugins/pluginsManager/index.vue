<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  RefreshIcon,
  UserIcon,
  LinkIcon,
  InfoCircleIcon,
  HomeIcon,
  ExtensionIcon,
  BookFilledIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import Result from '@/components/result/index.vue';
import { useUserStore } from '@/store';
import { PluginListModel } from '@/api/model/plugins';
import { getPluginList } from '@/api/plugins';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';

const userStore = useUserStore();
const loading = ref(true);
const isError = ref(false);
const plugins = ref<PluginListModel[]>([]);

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

const resolveUrl = (path: string) => {
  if (!path) return '';
  if (path.startsWith('http')) return path;
  const { baseUrl } = userStore;
  const root = baseUrl || window.location.origin;
  return `${root}${path.startsWith('/') ? '' : '/'}${path}`;
};

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-5 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">插件管理</h2>
        <div class="leading-relaxed mt-1 ext-sm text-[var(--td-text-color-secondary)] m-0">
          将插件文件放置于守护进程的
          <code
            class="bg-blue-100/60 dark:bg-blue-800/60 px-1.5 py-0.5 rounded text-xs mx-1 font-mono font-bold text-blue-600 dark:text-blue-300"
            >DaemonData/Plugins</code
          >
          目录，系统启动时会自动加载。<br class="hidden sm:block" />
          官方在线插件市场正在紧锣密鼓地开发中，敬请期待！
        </div>
      </div>

      <div class="flex items-center gap-2">
        <t-button variant="dashed" @click="getList">
          <template #icon><refresh-icon /></template>
          刷新列表
        </t-button>
        <t-button theme="primary" @click="changeUrl(DOC_URLS.plugin_dev)">
          <template #icon><book-filled-icon /></template>
          插件开发指北
        </t-button>
      </div>
    </div>

    <div class="relative min-h-[400px]">
      <div v-if="loading" class="flex flex-col items-center justify-center py-24">
        <t-loading size="medium" text="正在扫描已安装插件..." />
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
        <result title="未发现插件" tip="当前系统目录暂无已安装的插件" type="404" />
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
                class="w-16 h-16 rounded-xl border border-[var(--td-component-border)] overflow-hidden bg-zinc-50 dark:bg-zinc-900/50 flex items-center justify-center shadow-inner group-hover:shadow-md transition-shadow"
              >
                <img
                  v-if="item.icon"
                  :src="resolveUrl(item.icon)"
                  class="w-full h-full object-cover"
                  alt="plugin-icon"
                  @error="(e) => ((e.target as HTMLImageElement).src = 'https://www.mslmc.cn/logo.png')"/>
                <extension-icon v-else size="32px" class="text-zinc-400" />
              </div>
            </div>

            <div class="flex-grow min-w-0 flex flex-col gap-1.5">
              <div class="flex items-center gap-3">
                <h3 class="text-base font-bold text-[var(--td-text-color-primary)] truncate m-0 tracking-tight">
                  {{ item.name }}
                </h3>
                <t-tag size="small" variant="light-outline" theme="primary" class="!px-2 !rounded-md">
                  v{{ item.version }}
                </t-tag>
                <span class="text-xs font-mono text-[var(--td-text-color-secondary)] opacity-60"
                  >ID: {{ item.id }}</span
                >
              </div>
              <p class="text-sm text-[var(--td-text-color-secondary)] m-0 leading-relaxed line-clamp-2">
                {{ item.description || '该插件暂无详细说明。' }}
              </p>
            </div>

            <div
              class="shrink-0 flex flex-wrap md:flex-nowrap items-center gap-6 md:pl-6 md:border-l border-dashed border-zinc-200 dark:border-zinc-700/60 mt-2 md:mt-0 pt-3 md:pt-0 border-t md:border-t-0 w-full md:w-auto"
            >
              <div class="flex flex-col gap-1.5 min-w-[100px]">
                <span
                  class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                >
                  DEVELOPER
                </span>
                <div class="flex items-center gap-2 text-[var(--td-text-color-primary)]">
                  <user-icon size="14px" class="text-[var(--color-primary)] opacity-70" />
                  <a
                    v-if="item.authorUrl"
                    :href="item.authorUrl"
                    target="_blank"
                    class="text-sm font-bold hover:text-[var(--color-primary)] transition-colors cursor-pointer decoration-none"
                  >
                    {{ item.developer }}
                  </a>
                  <span v-else class="text-sm font-bold">{{ item.developer }}</span>
                </div>
              </div>

              <div class="flex flex-col gap-1.5">
                <span
                  class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                >
                  MIN SDK
                </span>
                <div class="flex items-center gap-2 text-[var(--td-text-color-primary)]">
                  <info-circle-icon size="14px" class="text-orange-400 opacity-70" />
                  <span class="text-sm font-mono font-bold">{{ item.minSDKVersion }}</span>
                </div>
              </div>

              <div class="flex items-center gap-2 ml-auto">
                <t-tooltip v-if="item.pluginUrl" content="插件主页">
                  <t-button
                    shape="square"
                    variant="outline"
                    size="small"
                    :href="item.pluginUrl"
                    target="_blank"
                    class="!rounded-lg hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)] transition-colors"
                  >
                    <template #icon><link-icon /></template>
                  </t-button>
                </t-tooltip>
                <t-tooltip v-if="item.authorUrl" content="开发者主页">
                  <t-button
                    shape="square"
                    variant="outline"
                    size="small"
                    :href="item.authorUrl"
                    target="_blank"
                    class="!rounded-lg hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)] transition-colors"
                  >
                    <template #icon><home-icon /></template>
                  </t-button>
                </t-tooltip>
              </div>
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
