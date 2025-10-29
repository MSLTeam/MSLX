<script setup lang="ts">
import { MessagePlugin } from 'tdesign-vue-next';
</script>

<template>
  <div>
    <t-card>
      <div>
        <t-button @click="MessagePlugin.success('喵喵喵')">喵喵喵</t-button>
      </div>
    </t-card>
  </div>
</template>

<style scoped lang="less"></style>
