<script setup lang="ts">
import { computed, ref, watch, onUnmounted } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { getDockerPullTask, postPullDockerImage } from '@/api/docker';
import { DockerPullTaskModel } from '@/api/model/docker';

const props = defineProps<{
  visible: boolean;
  presetImage?: string;
}>();

const emit = defineEmits<{
  'update:visible': [value: boolean];
  finished: [];
}>();

const image = ref('');
const platform = ref('');
const submitting = ref(false);
const task = ref<DockerPullTaskModel | null>(null);
let timer: ReturnType<typeof setTimeout> | null = null;

const isRunning = computed(() => task.value?.status === 'pending' || task.value?.status === 'processing');

const progressStatus = computed(() => {
  if (task.value?.status === 'error') return 'error';
  if (task.value?.status === 'success') return 'success';
  return 'active';
});

const stopPolling = () => {
  if (timer) {
    clearTimeout(timer);
    timer = null;
  }
};

const pollTask = async (taskId: string) => {
  try {
    const res = await getDockerPullTask(taskId);
    task.value = res;

    if (res.status === 'pending' || res.status === 'processing') {
      timer = setTimeout(() => pollTask(taskId), 1500);
      return;
    }

    if (res.status === 'success') {
      MessagePlugin.success(res.message || '拉取完成');
      emit('finished');
    } else {
      MessagePlugin.error(res.message || '拉取失败');
    }
  } catch (error: any) {
    stopPolling();
    task.value = task.value
      ? { ...task.value, status: 'error', message: `进度查询失败：${error?.message ?? error}` }
      : null;
    MessagePlugin.error(`进度查询失败：${error?.message ?? error}`);
  }
};

const handleSubmit = async () => {
  const target = image.value.trim();
  if (!target) {
    MessagePlugin.warning('请输入镜像名称');
    return;
  }

  submitting.value = true;
  stopPolling();
  task.value = null;

  try {
    const res = await postPullDockerImage({
      image: target,
      platform: platform.value.trim() || undefined,
    });
    await pollTask(res.taskId);
  } catch (error: any) {
    MessagePlugin.error(`提交拉取任务失败：${error?.message ?? error}`);
  } finally {
    submitting.value = false;
  }
};

const handleClose = () => {
  if (isRunning.value) {
    MessagePlugin.info('拉取任务会在后台继续执行，可稍后刷新列表查看结果');
  }
  stopPolling();
  emit('update:visible', false);
};

watch(
  () => props.visible,
  (visible) => {
    if (visible) {
      image.value = props.presetImage || '';
      platform.value = '';
      task.value = null;
      // 内置的运行时 直接拉
      if (props.presetImage) handleSubmit();
    } else {
      stopPolling();
    }
  },
);

onUnmounted(stopPolling);
</script>

<template>
  <t-dialog
    :visible="props.visible"
    header="拉取 Docker 镜像"
    width="640px"
    :close-on-overlay-click="false"
    :footer="false"
    @close="handleClose"
  >
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium text-[var(--td-text-color-primary)]">镜像名称</span>
        <t-input
          v-model="image"
          placeholder="例如 itzg/minecraft-server:latest，不填标签默认 latest"
          :disabled="isRunning"
          @enter="handleSubmit"
        />
        <span class="text-xs text-[var(--td-text-color-placeholder)]">
          支持 MSLX 内置运行时伪协议（MSLX://DockerImage/Java/21），也支持私有仓库地址
        </span>
      </div>

      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium text-[var(--td-text-color-primary)]">平台（可选）</span>
        <t-input v-model="platform" placeholder="例如 linux/amd64，留空由 Docker 自行判断" :disabled="isRunning" />
      </div>

      <div class="flex items-center gap-3">
        <t-button :loading="submitting || isRunning" @click="handleSubmit">
          {{ isRunning ? '拉取中...' : '开始拉取' }}
        </t-button>
        <t-button variant="outline" @click="handleClose">关闭</t-button>
      </div>

      <div v-if="task" class="flex flex-col gap-2 pt-2 border-t border-dashed border-[var(--td-component-border)]">
        <div class="flex items-center justify-between gap-3">
          <span class="text-xs font-mono text-[var(--td-text-color-secondary)] truncate" :title="task.image">
            {{ task.image }}
          </span>
          <t-tag
            size="small"
            variant="light"
            :theme="task.status === 'error' ? 'danger' : task.status === 'success' ? 'success' : 'primary'"
          >
            {{ task.status === 'success' ? '完成' : task.status === 'error' ? '失败' : '进行中' }}
          </t-tag>
        </div>

        <t-progress :percentage="task.progress" :status="progressStatus as any" />

        <span class="text-xs text-[var(--td-text-color-secondary)] break-all">{{ task.message }}</span>

        <div
          v-if="task.logs?.length"
          class="max-h-52 overflow-auto rounded-lg bg-zinc-950/90 p-3 font-mono text-[11px] leading-relaxed text-zinc-200"
        >
          <div v-for="(line, idx) in task.logs" :key="idx" class="break-all">{{ line }}</div>
        </div>
      </div>
    </div>
  </t-dialog>
</template>
