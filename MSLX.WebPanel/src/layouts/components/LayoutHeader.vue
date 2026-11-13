<template>
  <l-header
    v-if="settingStore.showHeader"
    :show-logo="settingStore.showHeaderLogo"
    :theme="settingStore.displayMode"
    :layout="settingStore.effectiveLayout"
    :is-fixed="settingStore.isHeaderFixed"
    :menu="headerMenu"
    :is-compact="settingStore.isSidebarCompact"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { usePermissionStore, useSettingStore } from '@/store';
import LHeader from './Header.vue';

const permissionStore = usePermissionStore();
const settingStore = useSettingStore();
const { routers: menuRouters } = storeToRefs(permissionStore);
const headerMenu = computed(() => {
  if (settingStore.effectiveLayout === 'mix') {
    if (settingStore.splitMenu) {
      return menuRouters.value.map((menu) => ({
        ...menu,
        children: [],
      }));
    }
    return [];
  }
  return menuRouters.value as any;
});
</script>
