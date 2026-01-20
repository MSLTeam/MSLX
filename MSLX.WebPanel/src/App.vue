<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue';
import { useSettingStore, useUpdateStore } from '@/store';
import UpdateModal from '@/components/UpdateModal.vue';

const store = useSettingStore();
const updateStore = useUpdateStore();

const mode = computed(() => store.displayMode);

// 监听主题变化的对象
const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');

// handle变更主题
const handleSystemChange = (e: MediaQueryListEvent) => {
  store.setSystemTheme(e.matches ? 'dark' : 'light');
};

onMounted(() => {
  updateStore.checkAppUpdate(false);

  // 挂载监听
  mediaQuery.addEventListener('change', handleSystemChange);

  // 初始化校准
  store.setSystemTheme(mediaQuery.matches ? 'dark' : 'light');
});

onUnmounted(() => {
  mediaQuery.removeEventListener('change', handleSystemChange);
});
</script>

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

<style lang="less" scoped>
#nprogress .bar {
  background: var(--td-brand-color) !important;
}
</style>
