<script setup lang="ts">
import { reactive, ref } from 'vue';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';
import { type FormProps, FormRules, MessagePlugin } from 'tdesign-vue-next';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { changeUrl } from '@/router';

const isSuccess = ref(false);
const createdServerId = ref(0);
const formRef = ref(null);
const formData = reactive(<CreateInstanceQucikModeModel>{
  name: '',
  path: null,
  java: 'none',
  core: 'none',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
  packageFileKey: '',
  minM: 1027, // 因为不能不填这两内存 只能用magic number代替咯～
  maxM: 1027,
  args: '',
});

const rules: FormRules<CreateInstanceQucikModeModel> = {
  name: [{ required: true, message: '请输入服务器名称', type: 'error' }],
  args: [{ required: true, message: '请输入自定义启动参数', type: 'error' }],
};

const onSubmit: FormProps['onSubmit'] = async ({ validateResult }) => {
  if (validateResult === true) {
    try{
      const res = await postCreateInstanceQuickMode(formData);
      createdServerId.value = res.serverId;
      MessagePlugin.success('创建成功');
      isSuccess.value = true;
    }catch (e){
      MessagePlugin.error('创建失败！' + e.message);
    }
  } else {
    MessagePlugin.warning('请检查表单填写');
  }
};
</script>

<template>
  <div>
    <t-card v-if="!isSuccess" :bordered="false">
      <template #header>
        <span style="font-weight: bold; font-size: 16px">自定义启动指令模式</span>
      </template>

      <t-form ref="formRef" :rules="rules" :data="formData" label-align="top" @submit="onSubmit">
        <t-form-item label="服务器名称" name="name">
          <t-input v-model="formData.name" placeholder="给你的服务器起一个名字" />
        </t-form-item>

        <t-form-item label="存储路径(可选)" name="path" help="不填写将使用默认的保存路径。">
          <t-input v-model="formData.path" placeholder="请填写服务端保存位置" />
        </t-form-item>

        <t-form-item label="启动指令" name="args" help="此模式不会自动帮您配置Java环境，您需要填写完整的启动命令。">
          <t-textarea
            v-model="formData.args"
            placeholder="start.bat..."
            :autosize="{ minRows: 5, maxRows: 25 }"
            class="code-font-textarea"
          />
        </t-form-item>

        <t-form-item>
          <t-space>
            <t-button theme="primary" type="submit">提交创建</t-button>
          </t-space>
        </t-form-item>
      </t-form>
    </t-card>

    <div v-else class="result-success">
      <t-icon class="result-success-icon" name="check-circle" />
      <div class="result-success-title">服务器 ({{ createdServerId }}) 已创建成功</div>
      <div class="result-success-describe">你现在可以去服务器列表启动它了</div>
      <div>
        <t-button @click="changeUrl('/instance/list')"> 返回服务端列表 </t-button>
        <t-button theme="default" @click="changeUrl(`/instance/console/${createdServerId}`)"> 前往控制台 </t-button>
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre;
}

.result-success {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  min-height: 50vh;
  padding: 32px 0;

  &-icon {
    font-size: 64px;
    color: var(--td-success-color);
  }

  &-title {
    margin-top: 16px;
    font-size: 20px;
    color: var(--td-text-color-primary);
    text-align: center;
    line-height: 22px;
    font-weight: 500;
  }

  &-describe {
    margin: 8px 0 32px;
    font-size: 14px;
    color: var(--td-text-color-primary);
    line-height: 22px;
  }
}
</style>
