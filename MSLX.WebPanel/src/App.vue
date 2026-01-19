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
import { computed, onMounted } from 'vue';
import { useSettingStore, useUpdateStore } from '@/store';
import UpdateModal from '@/components/UpdateModal.vue';

const store = useSettingStore();
const updateStore = useUpdateStore();

const mode = computed(() => {
  return store.displayMode;
});

onMounted(() => {
  // 检查更新
  updateStore.checkAppUpdate(false);
});
</script>

<style lang="less" scoped>
#nprogress .bar {
  background: var(--td-brand-color) !important;
}
</style>
