<script setup lang="ts">
import { reactive, ref } from 'vue';
import { MessagePlugin, type FormProps, FormRules } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';

// 表单数据接口
interface FormData {
  name: string;
  type: 'toml' | 'ini';
  content: string;
}

const formRef = ref(null);

const formData = reactive<FormData>({
  name: '',
  type: 'toml',
  content: '',
});

const rules: FormRules<FormData> = {
  name: [{ required: true, message: '请输入隧道名称', type: 'error' }],
  content: [{ required: true, message: '配置文件内容不能为空', type: 'error' }],
};

const onSubmit: FormProps['onSubmit'] = async ({ validateResult }) => {
  if (validateResult === true) {
    await createFrpTunnel(formData.name, formData.content, 'Custom', formData.type);
  } else {
    MessagePlugin.warning('请检查表单填写');
  }
};

const onReset = () => {
  MessagePlugin.info('表单已重置');
};
</script>

<template>
  <div class="custom-frp-container">
    <t-card :bordered="false">
      <template #header>
        <span style="font-weight: bold; font-size: 16px">自定义 Frp 隧道</span>
      </template>

      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" @reset="onReset" @submit="onSubmit">
        <t-form-item label="隧道名称" name="name">
          <t-input v-model="formData.name" placeholder="请输入隧道名称" />
        </t-form-item>

        <t-form-item label="配置类型" name="type">
          <t-radio-group v-model="formData.type" variant="default-filled">
            <t-radio-button value="toml">TOML</t-radio-button>
            <t-radio-button value="ini">INI</t-radio-button>
          </t-radio-group>
        </t-form-item>

        <t-form-item label="隧道配置内容" name="content">
          <t-textarea
            v-model="formData.content"
            placeholder='serverAddr = "0.0.0.0"&#10;serverPort = 1027&#10;&#10;[[proxies]]&#10;name = "nahida_tcp"&#10;...'
            :autosize="{ minRows: 10, maxRows: 25 }"
            class="code-font-textarea"
          />
        </t-form-item>

        <t-form-item>
          <t-space>
            <t-button theme="primary" type="submit">保存配置</t-button>
            <t-button theme="default" variant="base" type="reset">重置</t-button>
          </t-space>
        </t-form-item>
      </t-form>
    </t-card>
  </div>
</template>

<style scoped lang="less">
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre;
}
</style>
