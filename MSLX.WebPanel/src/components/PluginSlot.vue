<template>
  <div v-if="extensions.length" class="flex flex-col gap-6 w-full">
    <div
      v-for="(ext, index) in extensions"
      :key="`${name}-ext-${index}`"
      :class="{ 'list-item-anim': animate }"
      :style="animate ? { animationDelay: getItemDelay(index) } : undefined"
      class="w-full"
    >
      <component :is="ext.component || ext" :ref="(el) => onRef(el, index)" v-bind="$attrs" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { usePluginUIStore } from '@/store';

const props = withDefaults(
  defineProps<{
    name: string;
    renderRef?: (el: any, index: number) => void;
    animate?: boolean;
    baseDelay?: number;
    step?: number;
  }>(),
  {
    animate: false,
    baseDelay: 0,
    step: 0.05,
  },
);

const pluginUIStore = usePluginUIStore();
const extensions = computed(() => pluginUIStore.extensions[props.name] || []);

const getItemDelay = (index: number) => {
  return `${(props.baseDelay + index * props.step).toFixed(2)}s`;
};

// 转发 ref
const onRef = (el: any, index: number) => {
  if (props.renderRef) {
    props.renderRef(el, index);
  }
};
</script>

<script lang="ts">
export default { inheritAttrs: false };
</script>

<style scoped>
.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
