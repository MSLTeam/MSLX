<script setup lang="ts">
import { ref, reactive } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { InstanceInfoModel } from '@/api/model/instance';

const props = defineProps<{
  serverId: number; // 接收 ID
}>();

// 定义向父组件发送的事件
const emit = defineEmits<{
  'success': []
}>();

// 状态管理
const visible = ref(false);
const loading = ref(false); // 保存按钮的 loading 状态
const formRef = ref(null);

// 表单数据
const formData = reactive({
  name: '',
  maxM: 1024,
  autoStart: false
});


const open = (currentData: InstanceInfoModel) => {
  formData.name = currentData?.name || '';
  formData.maxM = currentData?.maxM || 1024;
  formData.autoStart = false;

  visible.value = true;
};

// 提交保存
const onConfirm = async () => {
  // 简单校验
  if (!formData.name) {
    MessagePlugin.warning('请输入实例名称');
    return;
  }

  loading.value = true;

  try {
    // TODO: 服务器设置
    console.log(`[API] 提交设置 ServerID:${props.serverId}`, formData);

    await new Promise(resolve => setTimeout(resolve, 800)); // 模拟网络延迟

    MessagePlugin.success('配置保存成功');
    visible.value = false; // 关闭弹窗
    emit('success'); // 通知父组件刷新
  } catch (error) {
    MessagePlugin.error('保存失败，请重试' + error.message);
  } finally {
    loading.value = false;
  }
};

// 暴露方法给父组件
defineExpose({
  open
});
</script>

<template>
  <t-dialog
    v-model:visible="visible"
    header="实例配置"
    attach="body"
    :confirm-btn="{ content: '保存修改', loading: loading }"
    destroy-on-close
    @confirm="onConfirm"
  >
    <t-form ref="formRef" :data="formData" label-align="top">
      <t-form-item label="实例名称" name="name">
        <t-input v-model="formData.name" placeholder="给服务器起个名字" clearable />
      </t-form-item>

      <t-form-item label="最大内存分配 (Max RAM)" name="maxM">
        <t-input-number
          v-model="formData.maxM"
          :step="1024"
          :min="1024"
          suffix="MB"
          style="width: 100%"
          theme="column"
        />
        <div class="tips">建议分配物理内存的 70% 以下</div>
      </t-form-item>

      <t-form-item label="高级选项" name="autoStart">
        <div class="switch-row">
          <span>开机自启</span>
          <t-switch v-model="formData.autoStart" />
        </div>
      </t-form-item>
    </t-form>
  </t-dialog>
</template>

<style scoped lang="less">
.tips {
  font-size: 12px;
  color: var(--td-text-color-placeholder);
  margin-top: 4px;
}
.switch-row {
  display: flex; justify-content: space-between; align-items: center; width: 100%;
}
</style>
