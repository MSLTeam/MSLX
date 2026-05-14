<template>
  <component
    :is="ext.component || ext"
    v-for="(ext, index) in extensions"
    :key="`${name}-ext-${index}`"
    :ref="(el) => onRef(el, index)"
    v-bind="$attrs"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { usePluginUIStore } from '@/store';

const props = defineProps<{
  name: string;
  renderRef?: (el: any, index: number) => void;
}>();

const pluginUIStore = usePluginUIStore();
const extensions = computed(() => pluginUIStore.extensions[props.name] || []);

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
