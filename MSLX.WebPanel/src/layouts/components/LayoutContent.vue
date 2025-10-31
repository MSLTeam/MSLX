<template>
  <t-layout :class="`${prefix}-layout`">
    <t-content :class="`${prefix}-content-layout`">
      <l-breadcrumb v-if="settingStore.showBreadcrumb" />
      <l-content />
    </t-content>
    <t-footer v-if="settingStore.showFooter" :class="`${prefix}-footer-layout`">
      <l-footer />
    </t-footer>
  </t-layout>
</template>

<script setup lang="ts">
import { useSettingStore } from '@/store';
import { prefix } from '@/config/global';

import LContent from './Content.vue';
import LBreadcrumb from './Breadcrumb.vue';
import LFooter from './Footer.vue';


const settingStore = useSettingStore();

</script>
