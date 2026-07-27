<script setup lang="ts">
import { computed, ref } from 'vue';
import { getDockerImages } from '@/api/docker';

/**
 * 镜像输入框：手输任意镜像地址/从当前节点的本地镜像里挑
 */
const props = withDefaults(
  defineProps<{
    modelValue?: string;
    disabled?: boolean;
    placeholder?: string;
  }>(),
  {
    modelValue: '',
    disabled: false,
    placeholder: '输入或选择镜像，如 eclipse-temurin:21-jre',
  },
);

const emit = defineEmits<{
  'update:modelValue': [value: string];
}>();

const loading = ref(false);
const loaded = ref(false);
const errorTip = ref('');
const localImages = ref<{ value: string; size: string; usedCount: number }[]>([]);
// 用户手动输入的镜像，保留在下拉里以便回选
const customImages = ref<string[]>([]);

const value = computed({
  get: () => props.modelValue || '',
  set: (val: string) => emit('update:modelValue', val ?? ''),
});

const options = computed(() => {
  const list = localImages.value.map((item) => ({
    label: item.value,
    value: item.value,
    desc: item.usedCount > 0 ? `${item.size} · 已被 ${item.usedCount} 个实例使用` : item.size,
  }));

  const known = new Set(list.map((i) => i.value));

  // 当前值与历史手输值也放进选项
  [...customImages.value, value.value].forEach((item) => {
    if (item && !known.has(item)) {
      known.add(item);
      list.unshift({ label: item, value: item, desc: '自定义镜像' });
    }
  });

  return list;
});

const loadImages = async () => {
  if (loaded.value || loading.value || props.disabled) return;

  loading.value = true;
  errorTip.value = '';
  try {
    const images = await getDockerImages(false);
    localImages.value = (images || []).map((item) => ({
      value: item.reference,
      size: item.size,
      usedCount: item.usedBy?.length ?? 0,
    }));
    loaded.value = true;
  } catch (error: any) {
    errorTip.value = `无法读取本地镜像：${error?.message ?? error}`;
  } finally {
    loading.value = false;
  }
};

const handlePopupVisibleChange = (visible: boolean) => {
  if (visible) loadImages();
};

const handleCreate = (val: string) => {
  const image = String(val).trim();
  if (!image) return;

  if (!customImages.value.includes(image)) {
    customImages.value.push(image);
  }
  value.value = image;
};
</script>

<template>
  <t-select
    v-model="value"
    :options="options"
    :disabled="props.disabled"
    :placeholder="props.placeholder"
    :loading="loading"
    filterable
    creatable
    clearable
    class="!font-mono"
    @create="handleCreate"
    @popup-visible-change="handlePopupVisibleChange"
  >
    <template #option="{ option }">
      <div class="flex items-center justify-between gap-3 w-full">
        <span class="font-mono truncate">{{ option.label }}</span>
        <span class="text-[11px] text-[var(--td-text-color-placeholder)] shrink-0">{{ (option as any).desc }}</span>
      </div>
    </template>

    <template #empty>
      <div class="px-3 py-2 text-xs text-[var(--td-text-color-secondary)] leading-relaxed">
        {{ errorTip || '本地暂无镜像，直接输入镜像地址即可（回车确认）' }}
      </div>
    </template>
  </t-select>
</template>
