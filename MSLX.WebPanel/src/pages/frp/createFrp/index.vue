<script setup lang="ts">
import { ref } from 'vue';
import Custom from '@/pages/frp/createFrp/components/Custom.vue';
import MSLFrp from '@/pages/frp/createFrp/components/MSLFrp.vue';

const value = ref(1);
</script>

<template>
  <div>
    <t-card direction="vertical">
      <t-tabs v-model="value">
        <t-tab-panel :value="1" label="MSLFrp" :destroy-on-hide="false">
          <m-s-l-frp style="margin: 15px;"/>
        </t-tab-panel>
        <t-tab-panel :value="2" label="自定义Frp" :destroy-on-hide="false">
          <custom style="margin: 15px;"/>
        </t-tab-panel>
      </t-tabs>

    </t-card>
  </div>
</template>

<style scoped lang="less"></style>
