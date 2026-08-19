<script setup lang="ts">
import { computed } from 'vue';
import { useTaskStore } from '@/store';
import {
  CheckCircleFilledIcon,
  CloseCircleFilledIcon,
  TimeFilledIcon,
  DeleteIcon,
  MinusCircleIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import NodeSwitcher from '@/components/node-switcher/index.vue';

const props = defineProps<{
  visible: boolean;
}>();

const emit = defineEmits(['update:visible']);

const taskStore = useTaskStore();

const isVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val),
});

const getTaskTypeLabel = (type: number) => {
  switch (type) {
    case 0:
      return '压缩';
    case 1:
      return '解压';
    case 2:
      return '下载';
    default:
      return '任务';
  }
};

const handleCancel = async (id: string) => {
  try {
    await taskStore.cancelTask(id);
    MessagePlugin.success('已发送取消请求');
  } catch {
    /* empty */
  }
};

const handleDelete = async (id: string) => {
  try {
    await taskStore.deleteTask(id);
  } catch {
    /* empty */
  }
};

const handleClearFinished = async () => {
  try {
    await taskStore.clearFinished();
    MessagePlugin.success('已清理结束的任务');
  } catch {
    /* empty */
  }
};
</script>

<template>
  <t-drawer v-model:visible="isVisible" header="后台任务中心" size="350px" :footer="false">
    <div class="flex flex-col h-full">
      <div class="flex justify-between items-center mb-4 px-1 gap-2">
        <span class="text-sm text-[var(--td-text-color-secondary)] whitespace-nowrap">
          运行中: <span class="text-[var(--color-primary)] font-bold">{{ taskStore.runningCount }}</span>
        </span>
        <div class="flex-1 flex justify-end items-center gap-2">
          <node-switcher width="130px" />
          <t-button variant="text" theme="primary" size="small" @click="handleClearFinished" class="px-1">
            清空已结束
          </t-button>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto space-y-3 px-1 pb-4">
        <div v-if="taskStore.tasks.length === 0" class="flex flex-col items-center justify-center h-40 text-zinc-400">
          <t-icon name="task" class="text-4xl mb-2" />
          <span class="text-sm">暂无后台任务</span>
        </div>

        <div
          v-for="task in taskStore.tasks"
          :key="task.id"
          class="bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-xl p-3 shadow-sm flex flex-col gap-2 relative"
        >
          <div class="flex justify-between items-start gap-2">
            <div class="flex items-center gap-2 overflow-hidden">
              <span
                class="px-1.5 py-0.5 rounded bg-[var(--color-primary-light)] text-[var(--color-primary)] text-xs font-bold shrink-0"
              >
                {{ getTaskTypeLabel(task.type) }}
              </span>
              <span class="text-sm font-bold text-[var(--td-text-color-primary)] truncate" :title="task.title">
                {{ task.title }}
              </span>
            </div>

            <div class="shrink-0 flex items-center gap-1">
              <t-button
                v-if="task.state === 0 || task.state === 1"
                shape="square"
                variant="text"
                theme="danger"
                size="small"
                title="取消任务"
                @click="handleCancel(task.id)"
              >
                <minus-circle-icon />
              </t-button>
              <t-button
                v-else
                shape="square"
                variant="text"
                theme="default"
                size="small"
                title="清除记录"
                @click="handleDelete(task.id)"
              >
                <delete-icon />
              </t-button>
            </div>
          </div>

          <div class="text-xs text-[var(--td-text-color-secondary)] truncate">
            {{ task.message }}
          </div>

          <div class="flex items-center gap-2 mt-1">
            <div class="flex-1">
              <t-progress
                theme="line"
                :percentage="task.progress"
                :status="task.state === 3 ? 'error' : task.state === 2 ? 'success' : 'active'"
                :label="false"
              />
            </div>
            <div class="shrink-0 text-xs font-mono w-10 text-right">{{ task.progress }}%</div>
          </div>

          <div class="flex items-center gap-1 mt-1 text-xs">
            <time-filled-icon class="text-blue-500" v-if="task.state === 0 || task.state === 1" />
            <check-circle-filled-icon class="text-emerald-500" v-else-if="task.state === 2" />
            <close-circle-filled-icon class="text-red-500" v-else-if="task.state === 3" />
            <minus-circle-icon class="text-zinc-400" v-else-if="task.state === 4" />

            <span class="text-[var(--td-text-color-secondary)] font-medium">
              <template v-if="task.state === 0">排队中</template>
              <template v-else-if="task.state === 1">运行中</template>
              <template v-else-if="task.state === 2">已完成</template>
              <template v-else-if="task.state === 3">失败</template>
              <template v-else-if="task.state === 4">已取消</template>
            </span>
          </div>
        </div>
      </div>
    </div>
  </t-drawer>
</template>

<style scoped>
@import '@/style/tailwind/index.css';
</style>
