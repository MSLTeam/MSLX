<script setup lang="ts">
import { ref } from 'vue';
import QuickMode from './components/QuickMode.vue';
import CustomMode from './components/CustomMode.vue';
import PackageMode from './components/PackageMode.vue'

const value = ref(1);
</script>

<template>
  <div>
    <t-card direction="vertical">
      <t-tabs v-model="value">
        <t-tab-panel :value="1" label="快速模式" :destroy-on-hide="false">
          <quick-mode style="margin: 10px;"/>
        </t-tab-panel>
        <t-tab-panel :value="2" label="上传整合包模式" :destroy-on-hide="false">
          <package-mode style="margin: 10px;"/>
        </t-tab-panel>
        <t-tab-panel :value="3" label="自定义模式" :destroy-on-hide="false">
          <custom-mode style="margin: 10px;"/>
        </t-tab-panel>
      </t-tabs>

    </t-card>
  </div>
</template>

<style scoped lang="less"></style>
