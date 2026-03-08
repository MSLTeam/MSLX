<script setup lang="ts">
import { Tag as TTag, Icon as TIcon, Tooltip as TTooltip } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import pkg from '@/../package.json';
import { useInstanceListStore } from '@/store/modules/instance';
import { onMounted } from 'vue';
const userStore = useUserStore();
const instanceListStore = useInstanceListStore();

onMounted(() => {
  instanceListStore.refreshInstanceList();
});
</script>

<template>
  <div class="design-card w-full bg-white dark:bg-zinc-800 p-5 sm:p-6 rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">

    <div class="grid grid-cols-[repeat(auto-fill,minmax(260px,1fr))] gap-4">

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="server" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>在线实例：</span>
        </span>
        <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right break-all pl-2">
          {{ instanceListStore.onlineInstanceCount }} / {{ instanceListStore.totalInstanceCount }}
        </span>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="logo-codepen" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>NET环境：</span>
        </span>
        <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right break-all pl-2">
          {{ userStore.userInfo.systemInfo.netVersion }}
        </span>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="dashboard" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>面板版本：</span>
        </span>
        <t-tooltip :content="pkg.version" max-width="400px">
          <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right truncate min-w-0 pl-2">
            v{{ pkg.version }}
          </span>
        </t-tooltip>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="cloud" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>节点版本：</span>
        </span>
        <t-tooltip :content="`v${userStore.userInfo.version}`" max-width="400px">
          <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right truncate min-w-0 pl-2">
            v{{ userStore.userInfo.version }}
          </span>
        </t-tooltip>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="desktop" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>主机名：</span>
        </span>
        <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right truncate min-w-0 pl-2">
          {{ userStore.userInfo.systemInfo.hostname }}
        </span>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="system-code" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>系统类型：</span>
        </span>
        <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right break-all pl-2">
          {{ userStore.userInfo.systemInfo.osType }} ({{ userStore.userInfo.systemInfo.osArchitecture }})
        </span>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="system-setting" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>系统版本：</span>
        </span>
        <t-tooltip :content="userStore.userInfo.systemInfo.osVersion" max-width="400px">
          <span class="text-zinc-800 dark:text-zinc-200 font-semibold text-sm text-right truncate min-w-0 pl-2">
            {{ userStore.userInfo.systemInfo.osVersion }}
          </span>
        </t-tooltip>
      </div>

      <div class="flex items-center justify-between p-3.5 rounded-xl bg-zinc-50/50 dark:bg-zinc-900/30 border border-zinc-200/80 dark:border-zinc-700/60 hover:border-zinc-300 dark:hover:border-zinc-500 hover:bg-zinc-100/50 dark:hover:bg-zinc-800/40 transition-all group">
        <span class="flex items-center gap-2 text-zinc-500 dark:text-zinc-400 shrink-0 text-sm">
          <t-icon name="check-circle" class="text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors duration-300" />
          <span>版本匹配：</span>
        </span>
        <span class="flex justify-end pl-2">
          <t-tag
            :theme="pkg.version.startsWith(userStore.userInfo.targetFrontendVersion.panel) ? 'success' : 'danger'"
            shape="round"
          >
            {{ pkg.version.startsWith(userStore.userInfo.targetFrontendVersion.panel) ? '正确匹配' : '请更新' }}
          </t-tag>
        </span>
      </div>

    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
