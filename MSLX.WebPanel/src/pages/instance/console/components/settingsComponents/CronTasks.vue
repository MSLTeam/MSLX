<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { useRoute } from 'vue-router';
import {
  MessagePlugin,
  DialogPlugin,
  type FormRules,
  type FormInstanceFunctions,
} from 'tdesign-vue-next';
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
} from 'tdesign-icons-vue-next';

import { CronTaskItemModel } from '@/api/model/cronTasks';
import {
  getCronTasks,
  addCronTask,
  updateCronTask,
  deleteCronTask,
} from '@/api/cronTasks';

import CronGenerator from './CronGenerator.vue';

const route = useRoute();
const instanceId = computed(() => parseInt(route.params.id as string));

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
  payload: [{ validator: (val) => {
      if (formData.value.type === 'command' && !val) return { result: false, message: '命令内容不能为空', type: 'error' };
      return true;
    }, trigger: 'blur' }]
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
        formData.value.enable
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
        formData.value.enable
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
    onClose: () => confirmDialog.hide()
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
  <div class="page-container">

    <div class="page-header">
      <div class="section-title">定时计划任务</div>
      <t-button v-if="!isCreating" theme="primary" @click="handleStartCreate">
        <template #icon><add-icon /></template>
        创建新任务
      </t-button>
    </div>

    <transition name="slide-fade">
      <div v-if="isCreating" class="create-container">
        <div class="container-header">
          <span class="title">{{ isEditingId ? '编辑任务' : '创建新任务' }}</span>
          <t-button size="small" variant="text" @click="handleCancelCreate"><close-icon /></t-button>
        </div>

        <t-form ref="formRef" :data="formData" :rules="rules" label-width="0">

          <div class="setting-item">
            <div class="setting-info">
              <div class="title">任务名称</div>
              <div class="desc">给这个计划任务起个容易识别的名字，例如“每日自动重启”</div>
            </div>
            <div class="setting-control">
              <t-input v-model="formData.name" placeholder="请输入任务名称" />
            </div>
          </div>

          <div class="setting-item">
            <div class="setting-info">
              <div class="title">触发规则 (Cron)</div>
              <div class="desc">使用 Cron 表达式定义执行时间。支持秒级精度 (秒 分 时 日 月 周)</div>
            </div>
            <div class="setting-control">
              <t-input v-model="formData.cron" placeholder="例如: 0 0 12 * * ?" />
              <t-button variant="outline" theme="default" @click="showCronGen = true">
                表达式生成器
              </t-button>
            </div>
          </div>

          <div class="setting-item">
            <div class="setting-info">
              <div class="title">执行操作</div>
              <div class="desc">选择触发时要执行的动作类型</div>
            </div>
            <div class="setting-control">
              <t-select v-model="formData.type" :options="taskTypeOptions" />
            </div>
          </div>

          <div v-if="formData.type === 'command' || formData.type === 'restart'" class="setting-item">
            <div class="setting-info">
              <div class="title">{{ formData.type === 'restart' ? '重启提示语' : '控制台命令' }}</div>
              <div class="desc">
                {{ formData.type === 'restart' ? '重启前发送给全服玩家的倒计时提示消息' : '不需要加 /，直接输入命令内容' }}
              </div>
            </div>
            <div class="setting-control">
              <t-textarea v-model="formData.payload" :autosize="{ minRows: 2, maxRows: 5 }" placeholder="请输入内容..." />
            </div>
          </div>

          <div class="setting-item">
            <div class="setting-info">
              <div class="title">启用状态</div>
              <div class="desc">暂时禁用此任务而不删除它</div>
            </div>
            <div class="setting-control">
              <t-switch v-model="formData.enable" />
            </div>
          </div>

          <div class="form-actions">
            <t-button theme="primary" :loading="submitLoading" @click="handleSubmit">
              <template #icon><save-icon /></template>
              {{ isEditingId ? '保存修改' : '立即创建' }}
            </t-button>
            <t-button theme="default" variant="base" @click="handleCancelCreate">取消</t-button>
          </div>
        </t-form>
      </div>
    </transition>

    <t-loading :loading="loading" show-overlay>
      <div class="task-list">
        <div v-if="taskList.length === 0 && !loading" class="empty-state">
          暂无任务，请点击上方创建
        </div>

        <div v-for="item in taskList" :key="item.id" class="task-card">
          <div class="card-left">
            <div class="task-header-row">
              <t-tag size="small" :theme="item.enable ? 'success' : 'warning'" variant="light" class="status-tag">
                {{ item.enable ? '运行中' : '已暂停' }}
              </t-tag>
              <span class="task-name">{{ item.name }}</span>
            </div>
            <div class="task-meta-row">
              <t-tag size="small" variant="outline" :theme="getColorByType(item.type)" style="margin-right: 8px">
                <template #icon>
                  <component :is="getIconByType(item.type)" />
                </template>
                {{ item.type.toUpperCase() }}
              </t-tag>

              <span class="cron-display"><time-icon /> {{ item.cron }}</span>
            </div>
            <div class="task-payload" v-if="item.payload" :title="item.payload">
              {{ item.payload }}
            </div>
          </div>

          <div class="card-right">
            <t-button variant="text" theme="primary" @click="handleEdit(item)">
              <template #icon><edit-icon /></template> 编辑
            </t-button>
            <t-button variant="text" theme="danger" @click="handleDelete(item)">
              <template #icon><delete-icon /></template> 删除
            </t-button>
          </div>
        </div>
      </div>
    </t-loading>

    <cron-generator
      v-model:visible="showCronGen"
      :initial-value="formData.cron"
      @confirm="onCronGenerated"
    />

  </div>
</template>

<style scoped lang="less">
.page-container {
  max-width: 100%;
}

/* 顶部标题栏 - 这里进行了关键修改以匹配模板 */
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;

  /* 核心修复：复用 setting-group-title 的间距和边框样式 */
  margin-top: 32px;      /* 增加顶部距离，不再顶格 */
  margin-bottom: 16px;   /* 底部留白 */
  padding-bottom: 8px;   /* 文字和虚线的距离 */
  border-bottom: 1px dashed var(--td-component-stroke); /* 底部虚线 */
}

/* 蓝色竖条标题风格 - 保持不变，但对齐方式微调 */
.section-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  display: flex;
  align-items: center;

  &::before {
    content: '';
    display: inline-block;
    width: 4px;
    height: 16px;
    background-color: var(--td-brand-color);
    margin-right: 8px;
    border-radius: 2px;
  }
}

/* 创建/编辑容器 */
.create-container {
  background-color: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  padding: 0 0 24px 0;
  margin-bottom: 24px;
  overflow: hidden;
  /* 增加进入动画的平滑度 */
  transition: all 0.3s;

  .container-header {
    padding: 12px 24px;
    border-bottom: 1px solid var(--td-component-stroke);
    display: flex;
    justify-content: space-between;
    align-items: center;
    background-color: var(--td-bg-color-secondarycontainer);
    margin-bottom: 8px;

    .title {
      font-weight: 600;
      font-size: 14px;
    }
  }
}

/* 核心布局：Setting Item (左文右控) */
.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 24px;
  border-bottom: 1px dashed var(--td-component-stroke);

  &:last-child {
    border-bottom: none;
  }

  .setting-info {
    flex: 1;
    padding-right: 32px;
    max-width: 40%;

    .title {
      font-size: 14px;
      font-weight: 500;
      color: var(--td-text-color-primary);
      margin-bottom: 4px;
      line-height: 22px; /* 增加行高对齐 */
    }
    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      line-height: 20px;
    }
  }

  .setting-control {
    flex: 1;
    max-width: 60%;
    display: flex;
    justify-content: flex-end;
    align-items: center; /* 垂直居中 */
    gap: 8px; /* 输入框和按钮之间的间距 */

    /* 让输入框、下拉框占满剩余空间 */
    .t-input, .t-select, .t-textarea {
      flex: 1;       /* 关键：自动撑开宽度 */
      width: auto;   /* 覆盖之前的 width: 100% */
      max-width: 400px;
    }

    /* 防止按钮被压缩 */
    .t-button {
      flex-shrink: 0;
    }
  }
}

.form-actions {
  padding: 24px 24px 0 24px;
  display: flex;
  gap: 12px;
}

/* 列表样式 */
.task-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  /* 确保列表和标题之间有距离 */
  margin-top: 8px;
}

.task-card {
  background-color: var(--td-bg-color-container);
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  padding: 16px 24px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  transition: all 0.2s;

  &:hover {
    border-color: var(--td-brand-color);
  }

  .card-left {
    flex: 1;
    min-width: 0; /* 防止flex子项溢出 */

    .task-header-row {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 8px;

      .task-name {
        font-size: 16px;
        font-weight: 600;
        color: var(--td-text-color-primary);
      }
    }

    .task-meta-row {
      display: flex;
      align-items: center;
      margin-bottom: 6px;
      font-size: 13px;

      .cron-display {
        font-family: monospace; /* 等宽字体显示Cron */
        color: var(--td-text-color-secondary);
        background: var(--td-bg-color-secondarycontainer);
        padding: 2px 6px;
        border-radius: 4px;
        display: flex;
        align-items: center;
        gap: 6px;
      }
    }

    .task-payload {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      max-width: 600px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      line-height: 1.5;
    }
  }

  .card-right {
    display: flex;
    gap: 8px;
    flex-shrink: 0;
  }
}

.empty-state {
  text-align: center;
  padding: 40px;
  color: var(--td-text-color-placeholder);
  background: var(--td-bg-color-container);
  border-radius: var(--td-radius-medium);
  border: 1px dashed var(--td-component-stroke);
}

/* Vue Transition */
.slide-fade-enter-active,
.slide-fade-leave-active {
  transition: all 0.3s ease-out;
}
.slide-fade-enter-from,
.slide-fade-leave-to {
  transform: translateY(-10px);
  opacity: 0;
}

@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;
    padding: 16px;

    .setting-info {
      max-width: 100%;
      margin-bottom: 12px;
      padding-right: 0;
    }
    .setting-control {
      max-width: 100%;
      justify-content: flex-start;
      .t-input, .t-select, .t-textarea { max-width: 100%; }
    }
  }

  .task-card {
    flex-direction: column;
    align-items: flex-start;
    padding: 16px;

    .card-right {
      width: 100%;
      justify-content: flex-end;
      margin-top: 12px;
      border-top: 1px solid var(--td-component-stroke);
      padding-top: 8px;
    }
  }
}
</style>
