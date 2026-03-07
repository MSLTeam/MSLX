<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin, DialogPlugin, type FormRules, type FormInstanceFunctions } from 'tdesign-vue-next';
import {
  TimeIcon,
  AddIcon,
  EditIcon,
  DeleteIcon,
  PlayCircleIcon,
  StopCircleIcon,
  RefreshIcon,
  CodeIcon,
  CloseIcon,
  SaveIcon,
  BookIcon,
} from 'tdesign-icons-vue-next';

import { CronTaskItemModel } from '@/api/model/cronTasks';
import { getCronTasks, addCronTask, updateCronTask, deleteCronTask } from '@/api/cronTasks';

import CronGenerator from './CronGenerator.vue';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';

const route = useRoute();
const instanceId = computed(() => parseInt(route.params.serverId as string));

// --- 状态管理 ---
const taskList = ref<CronTaskItemModel[]>([]);
const loading = ref(false);
const isCreating = ref(false); // 控制顶部创建容器显示
const isEditingId = ref<string | null>(null); // 如果要支持内联编辑，可以扩展此逻辑，目前用于区分Create/Update
const submitLoading = ref(false);
const formRef = ref<FormInstanceFunctions | null>(null);
const showCronGen = ref(false); // Cron生成器弹窗

// 表单数据
const formData = ref({
  id: '',
  name: '',
  cron: '',
  type: 'command',
  payload: '',
  enable: true,
});

// 任务类型
const taskTypeOptions = [
  { label: '发送命令 (Command)', value: 'command' },
  { label: '备份存档 (Backup)', value: 'backup' },
  { label: '开启服务器 (Start)', value: 'start' },
  { label: '停止服务器 (Stop)', value: 'stop' },
  { label: '重启服务器 (Restart)', value: 'restart' },
];

// 校验规则
const rules: FormRules = {
  name: [{ required: true, message: '必填', trigger: 'blur' }],
  cron: [{ required: true, message: '必填', trigger: 'blur' }],
  type: [{ required: true, message: '必选', trigger: 'change' }],
  payload: [
    {
      validator: (val) => {
        if (formData.value.type === 'command' && !val)
          return { result: false, message: '命令内容不能为空', type: 'error' };
        return true;
      },
      trigger: 'blur',
    },
  ],
};

// --- 方法 ---

const fetchData = async () => {
  if (!instanceId.value) return;
  loading.value = true;
  try {
    const res = await getCronTasks(instanceId.value);
    taskList.value = res || [];
  } catch (e: any) {
    MessagePlugin.error(e.message || '获取列表失败');
  } finally {
    loading.value = false;
  }
};

// 开启创建模式
const handleStartCreate = () => {
  if (isCreating.value) return; // 已经打开了
  formData.value = { id: '', name: '', cron: '', type: 'command', payload: '', enable: true };
  isEditingId.value = null;
  isCreating.value = true;
};

// 开启编辑模式
const handleEdit = (item: CronTaskItemModel) => {
  formData.value = {
    id: item.id,
    name: item.name,
    cron: item.cron,
    type: item.type.toLowerCase(),
    payload: item.payload,
    enable: item.enable,
  };
  isEditingId.value = item.id;
  isCreating.value = true; // 打开顶部容器
  // 滚动到顶部
  window.scrollTo({ top: 0, behavior: 'smooth' });
};

// 关闭创建/编辑容器
const handleCancelCreate = () => {
  isCreating.value = false;
  isEditingId.value = null;
};

// 提交保存
const handleSubmit = async () => {
  const result = await formRef.value?.validate();
  if (result !== true) return;

  submitLoading.value = true;
  try {
    if (isEditingId.value) {
      // 更新
      await updateCronTask(
        instanceId.value,
        formData.value.id,
        formData.value.name,
        formData.value.cron,
        formData.value.payload,
        formData.value.type,
        formData.value.enable,
      );
      MessagePlugin.success('更新成功');
    } else {
      // 创建
      await addCronTask(
        instanceId.value,
        formData.value.name,
        formData.value.cron,
        formData.value.payload,
        formData.value.type,
        formData.value.enable,
      );
      MessagePlugin.success('创建成功');
    }
    handleCancelCreate();
    fetchData();
  } catch (e: any) {
    MessagePlugin.error(e.message || '操作失败');
  } finally {
    submitLoading.value = false;
  }
};

// 删除
const handleDelete = (item: CronTaskItemModel) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除?',
    body: `确定删除任务 "${item.name}" 吗？`,
    theme: 'danger',
    onConfirm: async () => {
      try {
        await deleteCronTask(item.id);
        MessagePlugin.success('已删除');
        fetchData();
        confirmDialog.hide();
      } catch (e: any) {
        MessagePlugin.error(e.message);
      }
    },
    onClose: () => confirmDialog.hide(),
  });
};

// Cron生成器回调
const onCronGenerated = (cron: string) => {
  formData.value.cron = cron;
};

// 工具函数
const getIconByType = (type: string) => {
  const t = type.toLowerCase();
  if (t === 'start') return PlayCircleIcon;
  if (t === 'stop') return StopCircleIcon;
  if (t === 'restart') return RefreshIcon;
  return CodeIcon;
};
const getColorByType = (type: string) => {
  const t = type.toLowerCase();
  if (t === 'start') return 'success';
  if (t === 'stop') return 'danger';
  if (t === 'restart') return 'warning';
  return 'primary';
};

watch(() => instanceId.value, fetchData);
onMounted(fetchData);
</script>

<template>
  <div class="flex flex-col mx-auto w-full">
    <div class="flex items-center justify-between mt-5 mb-4 pb-2 border-b border-dashed border-zinc-200 dark:border-zinc-700">
      <div class="flex items-center gap-2">
        <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
        <h2 class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0">定时计划任务</h2>
      </div>

      <t-space v-if="!isCreating">
        <t-button theme="default" variant="outline" class="!rounded-lg" @click="changeUrl(DOC_URLS.cron)">
          <template #icon><book-icon /></template>使用文档
        </t-button>
        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleStartCreate">
          <template #icon><add-icon /></template>创建新任务
        </t-button>
      </t-space>
    </div>

    <transition
      enter-active-class="transition duration-300 ease-out"
      enter-from-class="transform -translate-y-2 opacity-0"
      leave-active-class="transition duration-200 ease-in"
      leave-to-class="transform -translate-y-2 opacity-0"
    >
      <div v-if="isCreating" class="mb-6 overflow-hidden bg-white dark:bg-zinc-900/40 border border-zinc-200 dark:border-zinc-800 rounded-xl shadow-sm">
        <div class="px-6 py-3 flex justify-between items-center bg-zinc-50 dark:bg-zinc-800/50 border-b border-zinc-200 dark:border-zinc-800">
          <span class="text-sm font-bold text-zinc-700 dark:text-zinc-200">{{ isEditingId ? '编辑任务' : '创建新任务' }}</span>
          <t-button size="small" variant="text" shape="square" @click="handleCancelCreate"><close-icon /></t-button>
        </div>

        <t-form ref="formRef" :data="formData" :rules="rules" label-width="0" class="p-0">
          <div v-for="(field, idx) in [
            { title: '任务名称', desc: '给计划任务起个易识别的名字', key: 'name' },
            { title: '触发规则 (Cron)', desc: '支持秒级精度 (秒 分 时 日 月 周)', key: 'cron' },
            { title: '执行操作', desc: '选择触发时要执行的动作类型', key: 'type' }
          ]" :key="idx" class="flex flex-col md:flex-row md:items-start justify-between p-5 border-b border-dashed border-zinc-100 dark:border-zinc-800 last:border-0">
            <div class="flex-1 md:max-w-[40%] pr-0 md:pr-8 mb-3 md:mb-0">
              <div class="text-sm font-bold text-zinc-800 dark:text-zinc-200">{{ field.title }}</div>
              <div class="text-xs text-zinc-500 dark:text-zinc-400 mt-1">{{ field.desc }}</div>
            </div>
            <div class="flex-1 md:max-w-[60%] w-full flex items-center gap-2">
              <t-input v-if="field.key === 'name'" v-model="formData.name" placeholder="请输入任务名称" class="flex-1" />
              <template v-if="field.key === 'cron'">
                <t-input v-model="formData.cron" placeholder="例如: 0 0 12 * * ?" class="flex-1" />
                <t-button variant="outline" class="shrink-0" @click="showCronGen = true">生成器</t-button>
              </template>
              <t-select v-if="field.key === 'type'" v-model="formData.type" :options="taskTypeOptions" class="w-full" />
            </div>
          </div>

          <div v-if="formData.type === 'command' || formData.type === 'restart'" class="flex flex-col md:flex-row md:items-start justify-between p-5 border-b border-dashed border-zinc-100 dark:border-zinc-800">
            <div class="flex-1 md:max-w-[40%] pr-0 md:pr-8 mb-3 md:mb-0">
              <div class="text-sm font-bold text-zinc-800 dark:text-zinc-200">{{ formData.type === 'restart' ? '重启提示语' : '控制台命令' }}</div>
              <div class="text-xs text-zinc-500 dark:text-zinc-400 mt-1">
                {{ formData.type === 'restart' ? '重启前发送给玩家的消息' : '直接输入内容，不需要加 /' }}
              </div>
            </div>
            <div class="flex-1 md:max-w-[60%] w-full">
              <t-textarea v-model="formData.payload" :autosize="{ minRows: 2, maxRows: 4 }" placeholder="请输入内容..." class="w-full" />
            </div>
          </div>

          <div class="flex items-center justify-between p-5">
            <div class="flex-1 pr-8">
              <div class="text-sm font-bold text-zinc-800 dark:text-zinc-200">启用状态</div>
              <div class="text-xs text-zinc-500 dark:text-zinc-400 mt-1">暂时禁用此任务而不删除它</div>
            </div>
            <t-switch v-model="formData.enable" />
          </div>

          <div class="px-5 py-4 bg-zinc-50/50 dark:bg-zinc-800/20 flex gap-3">
            <t-button theme="primary" :loading="submitLoading" class="!rounded-lg" @click="handleSubmit">
              <template #icon><save-icon /></template>{{ isEditingId ? '保存修改' : '立即创建' }}
            </t-button>
            <t-button theme="default" variant="base" class="!rounded-lg" @click="handleCancelCreate">取消</t-button>
          </div>
        </t-form>
      </div>
    </transition>

    <t-loading :loading="loading" show-overlay>
      <div class="flex flex-col gap-3 mt-2">
        <div v-if="taskList.length === 0 && !loading" class="flex flex-col items-center justify-center p-12 border-2 border-dashed border-zinc-200 dark:border-zinc-800 rounded-2xl text-zinc-400 dark:text-zinc-500">
          <span class="text-sm font-medium">暂无任务，请点击上方创建</span>
        </div>

        <div v-for="item in taskList" :key="item.id" class="group flex flex-col md:flex-row items-center justify-between p-5 bg-white dark:bg-zinc-900/40 border border-zinc-200 dark:border-zinc-800 rounded-xl transition-all duration-200 hover:border-[var(--color-primary)] hover:shadow-md">
          <div class="flex-1 min-w-0 w-full">
            <div class="flex items-center gap-3 mb-3">
              <t-tag size="small" :theme="item.enable ? 'success' : 'warning'" variant="light-outline" class="!rounded-md">
                {{ item.enable ? '运行中' : '已暂停' }}
              </t-tag>
              <span class="text-base font-bold text-zinc-800 dark:text-zinc-200 truncate">{{ item.name }}</span>
            </div>

            <div class="flex flex-wrap items-center gap-3 text-xs">
              <t-tag size="small" variant="outline" :theme="getColorByType(item.type)" class="!rounded-md uppercase font-mono">
                <template #icon><component :is="getIconByType(item.type)" /></template>
                {{ item.type }}
              </t-tag>

              <div class="flex items-center gap-1.5 px-2 py-1 bg-zinc-100 dark:bg-zinc-800 text-zinc-500 dark:text-zinc-400 rounded-md font-mono">
                <time-icon class="text-sm" /> {{ item.cron }}
              </div>
            </div>

            <div v-if="item.payload" class="mt-3 text-xs text-zinc-400 dark:text-zinc-500 bg-zinc-50 dark:bg-zinc-800/30 p-2 rounded-md border border-zinc-100 dark:border-zinc-800/50 truncate" :title="item.payload">
              {{ item.payload }}
            </div>
          </div>

          <div class="flex shrink-0 gap-1 mt-4 md:mt-0 pt-3 md:pt-0 border-t md:border-t-0 border-zinc-100 dark:border-zinc-800 w-full md:w-auto justify-end">
            <t-button variant="text" theme="primary" class="!rounded-lg hover:!bg-[var(--color-primary)]/10" @click="handleEdit(item)">
              <template #icon><edit-icon /></template> 编辑
            </t-button>
            <t-button variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="handleDelete(item)">
              <template #icon><delete-icon /></template> 删除
            </t-button>
          </div>
        </div>
      </div>
    </t-loading>

    <cron-generator v-model:visible="showCronGen" :initial-value="formData.cron" @confirm="onCronGenerated" />
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
