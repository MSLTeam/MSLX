<script setup lang="ts">
import { ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { getDockerImageDetail } from '@/api/docker';
import { DockerImageDetailModel } from '@/api/model/docker';

const props = defineProps<{
  visible: boolean;
  reference: string;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
}>();

const loading = ref(false);
const detail = ref<DockerImageDetailModel | null>(null);
const showRaw = ref(false);

const formatSize = (bytes: number) => {
  if (!bytes) return '-';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let idx = 0;
  while (value >= 1024 && idx < units.length - 1) {
    value /= 1024;
    idx += 1;
  }
  return `${value.toFixed(idx === 0 ? 0 : 2)} ${units[idx]}`;
};

const fetchDetail = async () => {
  loading.value = true;
  detail.value = null;
  showRaw.value = false;
  try {
    detail.value = await getDockerImageDetail(props.reference);
  } catch (error: any) {
    MessagePlugin.error(`获取镜像详情失败：${error?.message ?? error}`);
  } finally {
    loading.value = false;
  }
};

watch(
  () => props.visible,
  (visible) => {
    if (visible && props.reference) fetchDetail();
  },
);
</script>

<template>
  <t-dialog
    :visible="props.visible"
    header="镜像详情"
    width="720px"
    :footer="false"
    @close="emit('update:visible', false)"
  >
    <div v-if="loading" class="flex justify-center py-12">
      <t-loading size="small" text="读取镜像信息..." />
    </div>

    <div v-else-if="detail" class="flex flex-col gap-4 max-h-[65vh] overflow-auto pr-1">
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">镜像 ID</span>
          <span class="text-xs font-mono break-all">{{ detail.imageId }}</span>
        </div>
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">大小</span>
          <span class="text-xs font-mono">{{ formatSize(detail.size) }}</span>
        </div>
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">架构 / 系统</span>
          <span class="text-xs font-mono">{{ detail.architecture || '-' }} / {{ detail.os || '-' }}</span>
        </div>
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">创建时间</span>
          <span class="text-xs font-mono break-all">{{ detail.created || '-' }}</span>
        </div>
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">工作目录</span>
          <span class="text-xs font-mono">{{ detail.workingDir || '-' }}</span>
        </div>
        <div class="flex flex-col gap-1">
          <span class="text-xs text-[var(--td-text-color-placeholder)]">分层数</span>
          <span class="text-xs font-mono">{{ detail.layers?.length ?? 0 }}</span>
        </div>
      </div>

      <div v-if="detail.repoTags?.length" class="flex flex-col gap-2">
        <span class="text-sm font-medium">标签</span>
        <div class="flex flex-wrap gap-2">
          <t-tag v-for="tag in detail.repoTags" :key="tag" size="small" variant="light">{{ tag }}</t-tag>
        </div>
      </div>

      <div v-if="detail.entrypoint?.length || detail.cmd?.length" class="flex flex-col gap-2">
        <span class="text-sm font-medium">启动配置</span>
        <div class="rounded-lg bg-zinc-100 dark:bg-zinc-800/60 p-3 text-xs font-mono break-all flex flex-col gap-1">
          <div v-if="detail.entrypoint?.length">ENTRYPOINT: {{ detail.entrypoint.join(' ') }}</div>
          <div v-if="detail.cmd?.length">CMD: {{ detail.cmd.join(' ') }}</div>
        </div>
      </div>

      <div v-if="detail.exposedPorts?.length" class="flex flex-col gap-2">
        <span class="text-sm font-medium">暴露端口</span>
        <div class="flex flex-wrap gap-2">
          <t-tag v-for="port in detail.exposedPorts" :key="port" size="small" variant="outline">{{ port }}</t-tag>
        </div>
      </div>

      <div v-if="detail.volumes?.length" class="flex flex-col gap-2">
        <span class="text-sm font-medium">卷挂载点</span>
        <div class="flex flex-wrap gap-2">
          <t-tag v-for="vol in detail.volumes" :key="vol" size="small" variant="outline">{{ vol }}</t-tag>
        </div>
      </div>

      <div v-if="detail.env?.length" class="flex flex-col gap-2">
        <span class="text-sm font-medium">环境变量</span>
        <div
          class="max-h-40 overflow-auto rounded-lg bg-zinc-100 dark:bg-zinc-800/60 p-3 text-xs font-mono flex flex-col gap-1"
        >
          <div v-for="env in detail.env" :key="env" class="break-all">{{ env }}</div>
        </div>
      </div>

      <div class="flex flex-col gap-2">
        <t-button variant="dashed" size="small" @click="showRaw = !showRaw">
          {{ showRaw ? '隐藏' : '查看' }}原始 inspect 数据
        </t-button>
        <pre
          v-if="showRaw"
          class="max-h-72 overflow-auto rounded-lg bg-zinc-950/90 p-3 text-[11px] leading-relaxed text-zinc-200"
          >{{ detail.raw }}</pre
        >
      </div>
    </div>

    <t-empty v-else class="!bg-transparent" description="未获取到镜像信息" />
  </t-dialog>
</template>
