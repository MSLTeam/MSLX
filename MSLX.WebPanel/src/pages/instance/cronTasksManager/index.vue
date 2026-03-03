<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { MessagePlugin, DialogPlugin, type FormRules, type FormInstanceFunctions } from 'tdesign-vue-next';
import {
  CloudIcon,
  ServerIcon,
  TimeIcon,
  AddIcon,
  EditIcon,
  DeleteIcon,
  RefreshIcon,
  CodeIcon,
  PlayCircleIcon,
  StopCircleIcon,
} from 'tdesign-icons-vue-next';
import { useInstanceListStore, useUserStore } from '@/store';
import { getAllCronTasks, addCronTask, updateCronTask, deleteCronTask } from '@/api/cronTasks';
import { CronTaskItemModel } from '@/api/model/cronTasks';

import CronGenerator from '@/pages/instance/console/components/settingsComponents/CronGenerator.vue';

const instanceStore = useInstanceListStore();

// --- 状态管理 ---
const loading = ref(false);
const rawTasks = ref<CronTaskItemModel[]>([]);
const selectedRowKeys = ref<Record<number, string[]>>({}); // 存储每个实例选中的任务 ID 为 Key

// 弹窗表单状态
const formVisible = ref(false);
const isEdit = ref(false);
const submitLoading = ref(false);
const formRef = ref<FormInstanceFunctions | null>(null);
const showCronGen = ref(false);

const userStore = useUserStore();

const formData = ref({
  id: '',
  instanceId: undefined as number | undefined,
  name: '',
  cron: '',
  type: 'command',
  payload: '',
  enable: true,
});

const taskTypeOptions = [
  { label: '发送命令 (Command)', value: 'command' },
  { label: '备份存档 (Backup)', value: 'backup' },
  { label: '开启服务器 (Start)', value: 'start' },
  { label: '停止服务器 (Stop)', value: 'stop' },
  { label: '重启服务器 (Restart)', value: 'restart' },
];

const rules: FormRules = {
  instanceId: [{ required: true, message: '请选择归属实例', trigger: 'change' }],
  name: [{ required: true, message: '必填', trigger: 'blur' }],
  cron: [{ required: true, message: '必填', trigger: 'blur' }],
  type: [{ required: true, message: '必选', trigger: 'change' }],
  payload: [
    {
      validator: (val) => {
        if ((formData.value.type === 'command' || formData.value.type === 'restart') && !val) {
          return { result: false, message: '此类型下内容不能为空', type: 'error' };
        }
        return true;
      },
      trigger: 'blur',
    },
  ],
};

// 表格列定义
const columns = [
  { colKey: 'row-select', type: 'multiple', width: 30, fixed: 'left' },
  { colKey: 'name', title: '任务名称', ellipsis: true },
  { colKey: 'type', title: '类型', width: 100 },
  { colKey: 'cron', title: 'Cron 规则', width: 140 },
  { colKey: 'payload', title: '执行参数', ellipsis: true },
  { colKey: 'enable', title: '状态', width: 90 },
  { colKey: 'op', title: '操作', width: 140, fixed: 'right' },
];

const instanceOptions = computed(() => {
  return instanceStore.instanceList.map((inst) => ({
    label: `[${inst.id}] ${inst.name}`,
    value: inst.id,
  }));
});

// --- 数据归集 ---
const groupedInstances = computed(() => {
  const list = instanceStore.instanceList.map((inst) => {
    // 过滤出属于当前实例的任务
    const tasks = rawTasks.value.filter((t) => t.instanceId === inst.id);
    return {
      id: inst.id,
      name: inst.name,
      core: inst.core,
      tasks: tasks,
    };
  });

  // 排序：有任务的排前面
  return list.sort((a, b) => {
    const aHas = a.tasks && a.tasks.length > 0;
    const bHas = b.tasks && b.tasks.length > 0;
    if (aHas && !bHas) return -1;
    if (!aHas && bHas) return 1;
    return a.id - b.id;
  });
});

// --- 方法 ---

const fetchData = async () => {
  loading.value = true;
  try {
    await instanceStore.refreshInstanceList();
    const res = await getAllCronTasks();
    rawTasks.value = res || [];
  } catch (error: any) {
    MessagePlugin.error('获取任务列表失败: ' + (error.message || '未知错误'));
  } finally {
    loading.value = false;
  }
};

// 打开新增弹窗
const handleAdd = (instanceId?: number) => {
  isEdit.value = false;
  formData.value = {
    id: '',
    instanceId: instanceId,
    name: '',
    cron: '',
    type: 'command',
    payload: '',
    enable: true,
  };
  formVisible.value = true;
};

// 打开编辑弹窗
const handleEdit = (item: CronTaskItemModel) => {
  isEdit.value = true;
  formData.value = {
    id: item.id,
    instanceId: item.instanceId,
    name: item.name,
    cron: item.cron,
    type: item.type.toLowerCase(),
    payload: item.payload,
    enable: item.enable,
  };
  formVisible.value = true;
};

// 提交表单
const onDialogConfirm = async () => {
  const result = await formRef.value?.validate();
  if (result !== true) return;

  submitLoading.value = true;
  try {
    const { instanceId, id, name, cron, payload, type, enable } = formData.value;
    if (isEdit.value) {
      await updateCronTask(instanceId as number, id, name, cron, payload, type, enable);
      MessagePlugin.success('更新成功');
    } else {
      await addCronTask(instanceId as number, name, cron, payload, type, enable);
      MessagePlugin.success('创建成功');
    }
    formVisible.value = false;
    await fetchData();
  } catch (error: any) {
    MessagePlugin.error('保存失败: ' + (error.message || '未知错误'));
  } finally {
    submitLoading.value = false;
  }
};

// 切换启停状态
const handleToggleEnable = async (item: CronTaskItemModel, val: boolean) => {
  try {
    await updateCronTask(item.instanceId, item.id, item.name, item.cron, item.payload, item.type, val);
    MessagePlugin.success(`任务 [${item.name}] 已${val ? '启用' : '暂停'}`);
    item.enable = val;
  } catch (error: any) {
    MessagePlugin.error('状态更新失败: ' + (error.message || '未知错误'));
    await fetchData();
  }
};

// 删除逻辑
const processDelete = async (instanceId: number, taskIds: string[]) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除',
    body: `确定要删除选中的 ${taskIds.length} 个定时任务吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      confirmDialog.hide();
      const msg = MessagePlugin.loading('正在删除中...');
      try {
        const promises = taskIds.map((id) => deleteCronTask(id));
        await Promise.all(promises);

        MessagePlugin.success('删除成功');
        if (selectedRowKeys.value[instanceId]) {
          selectedRowKeys.value[instanceId] = [];
        }
        await fetchData();
      } catch (error: any) {
        MessagePlugin.error('部分任务删除失败，请重试 ' + error.message);
      } finally {
        MessagePlugin.close(msg);
      }
    },
  });
};

const handleDeleteOne = (instanceId: number, id: string) => {
  processDelete(instanceId, [id]);
};

const handleBatchDelete = (instanceId: number) => {
  const selected = selectedRowKeys.value[instanceId];
  if (!selected || selected.length === 0) {
    MessagePlugin.warning('请先选择要删除的任务');
    return;
  }
  processDelete(instanceId, selected);
};

// 表格选中事件
const onSelectChange = (value: string[], { _row }: any, instanceId: number) => {
  selectedRowKeys.value = {
    ...selectedRowKeys.value,
    [instanceId]: value,
  };
};

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

onMounted(() => {
  fetchData();
});
</script>

<template>
  <div class="backup-page-container">
    <div class="page-header">
      <div class="title-area">
        <h2 class="page-title">全局定时任务管理</h2>
        <p class="page-desc">集中管理所有服务器实例的 Cron 定时计划与调度策略</p>
      </div>
      <t-space>
        <t-button v-if="userStore.isAdmin" theme="primary" @click="handleAdd()">
          <template #icon><add-icon /></template>
          新增任务
        </t-button>
        <t-button theme="primary" variant="text" :loading="loading" @click="fetchData">
          <template #icon><refresh-icon /></template>
          刷新列表
        </t-button>
      </t-space>
    </div>

    <div v-if="loading && groupedInstances.length === 0" class="loading-wrapper">
      <t-loading text="加载数据中..." size="small"></t-loading>
    </div>

    <div v-else class="card-list">
      <transition-group name="list-anim">
        <div v-for="instance in groupedInstances" :key="instance.id" class="instance-card-wrapper">
          <t-card :bordered="false" class="instance-card" :class="{ 'is-empty': !instance.tasks?.length }">
            <div class="card-header">
              <div class="header-left">
                <t-tag theme="primary" variant="light" shape="mark">ID: {{ instance.id }}</t-tag>
                <div class="instance-info">
                  <h3 class="instance-name">
                    <server-icon class="icon-mr" />
                    {{ instance.name }}
                  </h3>
                  <span class="instance-core">
                    <cloud-icon class="icon-mr" />
                    {{ instance.core }}
                  </span>
                </div>
              </div>

              <div class="header-right">
                <t-space>
                  <t-button size="small" variant="outline" @click="handleAdd(instance.id)">
                    <template #icon><add-icon /></template> 添加
                  </t-button>
                  <t-tag v-if="instance.tasks?.length" theme="success" variant="outline">
                    {{ instance.tasks.length }} 个任务
                  </t-tag>
                  <t-tag v-else theme="default" disabled>无任务</t-tag>
                </t-space>
              </div>
            </div>

            <div v-if="instance.tasks?.length" class="backup-table-area">
              <div v-if="selectedRowKeys[instance.id]?.length > 0" class="table-toolbar">
                <span class="selected-count">已选 {{ selectedRowKeys[instance.id].length }} 项</span>
                <t-button theme="danger" variant="text" size="small" @click="handleBatchDelete(instance.id)">
                  批量删除
                </t-button>
              </div>

              <t-table
                row-key="id"
                :data="instance.tasks"
                :columns="columns as any"
                :selected-row-keys="selectedRowKeys[instance.id] || []"
                size="small"
                :hover="true"
                :pagination="instance.tasks.length > 5 ? { pageSize: 5 } : null"
                @select-change="(val, ctx) => onSelectChange(val as any, ctx, instance.id)"
              >
                <template #name="{ row }">
                  <div class="file-name-cell">
                    <time-icon class="task-icon icon-mr" style="color: var(--td-brand-color)" />
                    <span class="text" :title="row.name">{{ row.name }}</span>
                  </div>
                </template>

                <template #type="{ row }">
                  <t-tag size="small" variant="light" :theme="getColorByType(row.type)">
                    <template #icon>
                      <component :is="getIconByType(row.type)" />
                    </template>
                    {{ row.type.toUpperCase() }}
                  </t-tag>
                </template>

                <template #cron="{ row }">
                  <span style="font-family: monospace; color: var(--td-text-color-secondary)">{{ row.cron }}</span>
                </template>

                <template #enable="{ row }">
                  <t-switch
                    :value="row.enable"
                    size="small"
                    @change="(val) => handleToggleEnable(row, val as boolean)"
                  />
                </template>

                <template #op="{ row }">
                  <div class="op-buttons">
                    <t-button theme="primary" variant="text" size="small" @click="handleEdit(row)">
                      <template #icon><edit-icon /></template> 编辑
                    </t-button>
                    <t-button theme="danger" variant="text" size="small" @click="handleDeleteOne(instance.id, row.id)">
                      <template #icon><delete-icon /></template> 删除
                    </t-button>
                  </div>
                </template>
              </t-table>
            </div>

            <div v-else class="empty-backups">
              <span class="empty-text">当前实例暂无定时任务</span>
            </div>
          </t-card>
        </div>
      </transition-group>
    </div>

    <t-dialog
      v-model:visible="formVisible"
      :header="isEdit ? '编辑定时任务' : '新增定时任务'"
      width="600px"
      :confirm-btn="{ content: '保存', theme: 'primary', loading: submitLoading }"
      placement="center"
      :on-confirm="onDialogConfirm"
    >
      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" style="margin-top: 16px">
        <t-form-item label="归属实例" name="instanceId">
          <t-select
            v-model="formData.instanceId"
            :options="instanceOptions"
            placeholder="请选择要执行该任务的服务器实例"
            filterable
            :disabled="!!isEdit"
          />
        </t-form-item>

        <t-form-item label="任务名称" name="name">
          <t-input v-model="formData.name" placeholder="请输入任务名称，例如：凌晨自动重启" />
        </t-form-item>

        <t-form-item label="触发规则 (Cron 表达式)" name="cron">
          <t-input v-model="formData.cron" placeholder="例如: 0 0 4 * * ?">
            <template #suffix>
              <t-button variant="text" theme="primary" size="small" @click="showCronGen = true"> 生成器 </t-button>
            </template>
          </t-input>
        </t-form-item>

        <t-form-item label="执行操作类型" name="type">
          <t-select v-model="formData.type" :options="taskTypeOptions" />
        </t-form-item>

        <t-form-item
          v-if="formData.type === 'command' || formData.type === 'restart'"
          :label="formData.type === 'restart' ? '重启全服倒计时提示语' : '控制台执行命令'"
          name="payload"
        >
          <t-textarea
            v-model="formData.payload"
            :autosize="{ minRows: 2, maxRows: 5 }"
            placeholder="请输入执行内容..."
          />
        </t-form-item>

        <t-form-item label="初始状态" name="enable">
          <div style="display: flex; align-items: center; gap: 12px">
            <t-switch v-model="formData.enable" />
            <span style="color: var(--td-text-color-placeholder); font-size: 13px">
              {{ formData.enable ? '保存后立即生效运行' : '保存后处于暂停状态' }}
            </span>
          </div>
        </t-form-item>
      </t-form>
    </t-dialog>

    <cron-generator v-model:visible="showCronGen" :initial-value="formData.cron" @confirm="onCronGenerated" />
  </div>
</template>

<style lang="less" scoped>
/* 样式原封不动复刻你的备份页面样式，只改了几个微小的变量名映射 */
@card-bg: var(--td-bg-color-container);
@text-primary: var(--td-text-color-primary);
@text-secondary: var(--td-text-color-secondary);
@border-level: var(--td-component-stroke);

.backup-page-container {
  padding: 12px;
  margin: 0 auto;
  min-height: 100%;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;

  .title-area {
    .page-title {
      font-size: 24px;
      font-weight: 700;
      color: @text-primary;
      margin: 0 0 8px 0;
    }
    .page-desc {
      color: @text-secondary;
      font-size: 14px;
      margin: 0;
    }
  }
}

.loading-wrapper {
  display: flex;
  justify-content: center;
  padding: 48px;
}

.card-list {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

// 实例卡片样式
.instance-card {
  transition: all 0.3s ease;
  background: @card-bg;

  // 无任务时的样式降级
  &.is-empty {
    opacity: 0.8;
    .card-header {
      border-bottom: none;
    }
  }

  // 头部布局
  .card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding-bottom: 16px;
    border-bottom: 1px solid @border-level;
    flex-wrap: wrap;
    gap: 12px;

    .header-left {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;

      .instance-info {
        display: flex;
        align-items: baseline;
        gap: 12px;

        .instance-name {
          margin: 0;
          font-size: 18px;
          display: flex;
          align-items: center;
          color: @text-primary;
        }

        .instance-core {
          font-size: 13px;
          color: @text-secondary;
          background: var(--td-bg-color-secondarycontainer);
          padding: 2px 8px;
          border-radius: 4px;
          display: flex;
          align-items: center;
        }
      }
    }

    .header-right {
      display: flex;
      align-items: center;
    }
  }

  // 表格区域 (复用备份页面的类名)
  .backup-table-area {
    margin-top: 16px;

    .table-toolbar {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 8px;
      padding: 4px 8px;
      background: var(--td-error-color-1);
      border-radius: 4px;

      .selected-count {
        font-size: 12px;
        color: var(--td-error-color-7);
      }
    }

    .file-name-cell {
      display: flex;
      align-items: center;
      gap: 8px;
      .text {
        font-weight: 500;
      }
    }

    .op-buttons {
      display: flex;
      gap: 8px;
    }
  }

  .empty-backups {
    padding: 32px 0;
    text-align: center;
    color: var(--td-text-color-disabled);
    font-size: 13px;
  }
}

// 通用图标边距
.icon-mr {
  margin-right: 6px;
}

// 动画
.list-anim-move,
.list-anim-enter-active,
.list-anim-leave-active {
  transition: all 0.5s ease;
}
.list-anim-enter-from,
.list-anim-leave-to {
  opacity: 0;
  transform: translateY(20px);
}

// 移动端特定适配
@media (max-width: 600px) {
  .instance-card {
    .header-left {
      flex-direction: column;
      align-items: flex-start !important;
      gap: 8px !important;
    }
  }
}
</style>
