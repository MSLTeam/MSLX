<script setup lang="ts">
import { ref } from 'vue';
import { StoreIcon, SettingIcon, TimeIcon, Setting1Icon, BackupIcon, MoreIcon } from 'tdesign-icons-vue-next';

import GeneralSettings from './settingsComponents/GeneralSettings.vue';
import ModsPluginsManager from './settingsComponents/ModsPluginsManager.vue';
import ServerProperties from './settingsComponents/ServerProperties.vue';
import CronTasks from './settingsComponents/CronTasks.vue';
import BackupManager from './settingsComponents/BackupManager.vue';
import More from './settingsComponents/More.vue';

const visible = ref(false);
const currentTab = ref(0);

const menuItems = [
  { label: '实例设置', icon: SettingIcon },
  { label: '插件/模组', icon: StoreIcon },
  { label: '服务器属性', icon: Setting1Icon },
  { label: '定时任务', icon: TimeIcon },
  { label: '备份管理', icon: BackupIcon },
  { label: '更多功能', icon: MoreIcon },
];

const open = () => {
  visible.value = true;
};

defineExpose({ open });
</script>

<template>
  <t-dialog
    v-model:visible="visible"
    header="实例配置"
    width="90%"
    top="3vh"
    attach="body"
    :footer="false"
    class="settings-dialog"
  >
    <div class="flex flex-col md:flex-row h-[75vh] md:h-[72vh] overflow-hidden bg-white/50 dark:bg-zinc-900/30 rounded-b-xl">

      <div class="flex flex-row md:flex-col w-full md:w-40 shrink-0 border-b md:border-b-0 md:border-r border-zinc-200/60 dark:border-zinc-800/60 bg-zinc-50/50 dark:bg-zinc-950/30 overflow-x-auto md:overflow-y-auto hide-scrollbar md:pt-3">

        <div
          v-for="(item, index) in menuItems"
          :key="index"
          class="relative flex flex-col md:flex-row items-center justify-center md:justify-start flex-1 md:flex-none h-auto md:h-12 px-2 py-3 md:py-0 md:px-5 cursor-pointer text-xs md:text-sm transition-all duration-200 gap-1 md:gap-2.5 group"
          :class="currentTab === index ? 'text-[var(--color-primary)] font-bold bg-white/80 dark:bg-zinc-800/50 md:bg-transparent' : 'text-zinc-500 dark:text-zinc-400 hover:bg-zinc-200/50 dark:hover:bg-zinc-800/40'"
          @click="currentTab = index"
        >
          <div
            v-if="currentTab === index"
            class="absolute bottom-0 left-1/2 -translate-x-1/2 w-6 h-[3px] rounded-t-sm md:top-1/2 md:left-0 md:-translate-y-1/2 md:translate-x-0 md:w-1 md:h-6 md:rounded-r-sm md:rounded-tl-none bg-[var(--color-primary)] shadow-[0_0_8px_var(--color-primary)] opacity-80"
          ></div>

          <component :is="item.icon" class="text-xl md:text-lg shrink-0 transition-transform duration-300" :class="currentTab === index ? 'scale-110' : 'group-hover:scale-110'" />
          <span class="whitespace-nowrap overflow-hidden text-ellipsis">{{ item.label }}</span>
        </div>

      </div>

      <div class="flex-1 min-w-0 h-full flex flex-col relative bg-white/40 dark:bg-zinc-900/20">
        <div class="flex-1 overflow-y-auto custom-scrollbar p-4 pb-20 md:p-0 md:pl-8 md:pb-12 md:pr-2">

          <div v-if="currentTab === 0" class="tab-panel-anim"><general-settings /></div>
          <div v-if="currentTab === 1" class="tab-panel-anim"><mods-plugins-manager /></div>
          <div v-if="currentTab === 2" class="tab-panel-anim"><server-properties :instance-id="21" /></div>
          <div v-if="currentTab === 3" class="tab-panel-anim"><cron-tasks /></div>
          <div v-if="currentTab === 4" class="tab-panel-anim"><backup-manager /></div>
          <div v-if="currentTab === 5" class="tab-panel-anim"><more /></div>

        </div>
      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

.hide-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
  &::-webkit-scrollbar {
    display: none;
  }
}

.custom-scrollbar {
  .scrollbar-mixin();
}

.tab-panel-anim {
  animation: fadeSlideUp 0.3s cubic-bezier(0.2, 0.8, 0.2, 1) forwards;
  will-change: transform, opacity;
}

@keyframes fadeSlideUp {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
