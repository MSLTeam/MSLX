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
    <div class="flex justify-center items-center min-h-[200px] overflow-hidden bg-[#0000000a] w-full">
      <img
        v-if="imageBlobUrl"
        :src="imageBlobUrl"
        :alt="fileName"
        class="block max-w-full max-h-[70vh] object-contain"
      />
      <div v-else class="p-10 text-[var(--td-text-color-secondary)]">加载中...</div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

:deep(.t-dialog) {
  max-width: 90vw;
  min-width: 300px;
}

:deep(.t-dialog__body) {
  padding: 0;
}
</style>
