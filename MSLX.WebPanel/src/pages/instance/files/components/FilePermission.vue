<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { changeFileMode } from '@/api/files'; // 确保你的 API 定义了这个方法

const props = defineProps<{
  visible: boolean;
  instanceId: number;
  currentPath: string;
  targets: Array<{ name: string; fullPath: string; mode: string }>;
}>();

const emit = defineEmits(['update:visible', 'success']);

const formData = ref({
  mode: '755', // 默认
});

const loading = ref(false);

// 常用权限预设
const presets = [
  { label: '755 (推荐: 所有者读写执/他人读执)', value: '755' },
  { label: '777 (全开: 所有权限)', value: '777' },
  { label: '644 (普通文件: 读写/读)', value: '644' },
];

const title = computed(() => {
  if (props.targets.length === 1) {
    return `修改权限 - ${props.targets[0].name}`;
  }
  return `批量修改权限 (${props.targets.length} 项)`;
});

// 初始化数据
watch(
  () => props.visible,
  (val) => {
    if (val && props.targets.length > 0) {
      // 如果是单个文件，回显当前权限，否则默认 755
      if (props.targets.length === 1 && props.targets[0].mode && props.targets[0].mode !== 'Unknown') {
        formData.value.mode = props.targets[0].mode;
      } else {
        formData.value.mode = '755';
      }
    }
  }
);

const handleClose = () => {
  emit('update:visible', false);
  loading.value = false;
};

const handleConfirm = async () => {
  if (!formData.value.mode || !/^[0-7]{3}$/.test(formData.value.mode)) {
    MessagePlugin.warning('请输入正确的3位八进制权限码 (如 755)');
    return;
  }

  loading.value = true;
  let successCount = 0;
  let failCount = 0;

  try {
    // 循环处理所有选中的文件
    for (const target of props.targets) {
      try {
        await changeFileMode(instanceId, target.fullPath, formData.value.mode);
        successCount++;
      } catch (e) {
        failCount++;
        console.error(e);
      }
    }

    if (failCount === 0) {
      MessagePlugin.success('权限修改成功');
    } else {
      MessagePlugin.warning(`完成: 成功 ${successCount} 个, 失败 ${failCount} 个`);
    }

    emit('success');
    handleClose();
  } catch (err: any) {
    MessagePlugin.error(err.message || '请求失败');
  } finally {
    loading.value = false;
  }
};

const instanceId = props.instanceId;
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="title"
    :confirm-btn="{ content: '保存修改', loading: loading }"
    :on-close="handleClose"
    :on-confirm="handleConfirm"
    width="480px"
  >
    <div class="py-1 md:py-2">

      <t-form-item label="权限代码" required-mark>
        <t-input
          v-model="formData.mode"
          placeholder="例如: 755"
          tips="请输入3位八进制数字 (Linux Chmod)"
          class="[&_.t-input__inner]:!font-mono [&_.t-input__inner]:!text-[15px]"
        />
      </t-form-item>

      <div class="mt-5 p-4 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-inner">
        <div class="text-xs text-zinc-500 dark:text-zinc-400 mb-3 font-medium">快捷设置：</div>

        <div class="flex gap-2 flex-wrap mb-2.5">
          <t-tag
            v-for="pre in presets"
            :key="pre.value"
            variant="light-outline"
            theme="primary"
            class="cursor-pointer !font-mono !rounded-md hover:!bg-[var(--color-primary)] hover:!text-white transition-colors"
            @click="formData.mode = pre.value"
          >
            {{ pre.value }}
          </t-tag>
        </div>

        <div class="min-h-[16px] flex items-center">
          <div v-if="formData.mode === '755'" class="text-xs text-zinc-500 dark:text-zinc-400">
            适合可执行程序、脚本或文件夹
          </div>
          <div v-else-if="formData.mode === '644'" class="text-xs text-zinc-500 dark:text-zinc-400">
            适合普通配置文件、日志等
          </div>
          <div v-else-if="formData.mode === '777'" class="text-xs text-amber-600 dark:text-amber-500 font-medium">
            ⚠️ 允许任何人读写执行 (极其不安全)
          </div>
          <div v-else class="text-xs text-zinc-400 dark:text-zinc-500 italic opacity-60">
            自定义权限
          </div>
        </div>
      </div>

    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
