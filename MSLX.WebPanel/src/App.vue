<template>
  <router-view :class="[mode]" />
  <update-modal
    :visible="updateStore.showUpdateModal"
    :update-info="updateStore.updateInfo"
    :download-info="updateStore.downloadInfo"
    @close="updateStore.showUpdateModal = false"
    @skip="updateStore.handleSkipVersion"
  />
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, nextTick } from 'vue';
import { useSettingStore, useUpdateStore } from '@/store';
import UpdateModal from '@/components/UpdateModal.vue';

const store = useSettingStore();
const updateStore = useUpdateStore();

const mode = computed(() => {
  return store.displayMode;
});

// 监听系统主题变化
const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

const handleSystemChange = (e: MediaQueryListEvent) => {
  // auto才操作
  if (store.mode === 'auto') {
    const isSystemDark = e.matches;

    // 强制把 Store 设置成当前的系统色 (Dark/Light)
    store.updateConfig({ ...store.$state, mode: isSystemDark ? 'dark' : 'light' });

    // 改回 Auto
    nextTick(() => {
      store.updateConfig({ ...store.$state, mode: 'auto' });
    });
  }
};

onMounted(() => {
  updateStore.checkAppUpdate(false);

  // 挂载监听
  mediaQuery.addEventListener('change', handleSystemChange);
});

onUnmounted(() => {
  mediaQuery.removeEventListener('change', handleSystemChange);
});
</script>

<style lang="less" scoped>
#nprogress .bar {
  background: var(--td-brand-color) !important;
}
</style>
