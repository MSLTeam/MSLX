<script setup lang="ts">
defineProps<{
  visible: boolean;
  fileName: string;
  imageBlobUrl: string; // 接收父组件传来的 Blob URL
}>();

const emit = defineEmits(['update:visible']);

const handleClose = () => {
  emit('update:visible', false);
};
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="fileName"
    :footer="false"
    width="auto"
    top="10vh"
    class="image-preview-dialog"
    @close="handleClose"
  >
    <div class="image-wrapper">
      <img v-if="imageBlobUrl" :src="imageBlobUrl" :alt="fileName" />
      <div v-else class="loading-placeholder">加载中...</div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
:deep(.t-dialog) {
  max-width: 90vw;
  min-width: 300px;
}

:deep(.t-dialog__body) {
  padding: 0;
  overflow: hidden;
  display: flex;
  justify-content: center;
  background-color: #0000000a;
}

.image-wrapper {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;

  img {
    max-width: 100%;
    max-height: 70vh;
    object-fit: contain;
    display: block;
  }
}

.loading-placeholder {
  padding: 40px;
  color: var(--td-text-color-secondary);
}
</style>
