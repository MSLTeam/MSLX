<script setup lang="ts">
import { reactive, ref } from 'vue';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';
import { type FormProps, FormRules, MessagePlugin } from 'tdesign-vue-next';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { changeUrl } from '@/router';
import { useUserStore } from '@/store';
const userStore = useUserStore();

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
  ignoreEula: true,
});

const rules: FormRules<CreateInstanceQucikModeModel> = {
  name: [{ required: true, message: '请输入服务器名称', type: 'error' }],
  args: [{ required: true, message: '请输入自定义启动参数', type: 'error' }],
};

const onSubmit: FormProps['onSubmit'] = async ({ validateResult }) => {
  if (validateResult === true) {
    try {
      const res = await postCreateInstanceQuickMode(formData);
      createdServerId.value = res.serverId;
      MessagePlugin.success('创建成功');
      isSuccess.value = true;
    } catch (e) {
      MessagePlugin.error('创建失败！' + e.message);
    }
  } else {
    MessagePlugin.warning('请检查表单填写');
  }
};

const goToHome = () => {
  isSuccess.value = false;

  Object.assign(formData, {
    name: '新建服务器',
    path: null,
    java: 'none',
    core: 'none',
    packageFileKey: '',
    coreFileKey: '',
    coreUrl: '',
    coreSha256: '',
    minM: 1027,
    maxM: 1027,
    args: '',
    ignoreEula: true,
  });
};
</script>

<template>
  <div class="design-card list-item-anim bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-3xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6 sm:p-8">

    <div v-if="!isSuccess" class="flex flex-col relative pt-1">
      <t-form
        ref="formRef"
        :rules="rules"
        :data="formData"
        label-align="top"
        class="flex-1 flex flex-col [&_.t-form__item]:!mb-6"
        @submit="onSubmit"
      >
        <t-form-item label="实例名称" name="name">
          <t-input v-model="formData.name" placeholder="给你的服务器起一个名字" class="!w-full sm:!w-[28rem]" />
        </t-form-item>

        <t-form-item
          label="存储路径 (可选)"
          name="path"
          :help="userStore.userInfo.systemInfo.docker ? '您正在使用Docker容器部署，为保数据安全，仅支持使用默认数据路径' : '选填，留空将使用默认路径'"
        >
          <t-input
            v-model="formData.path"
            :disabled="userStore.userInfo.systemInfo.docker"
            placeholder="请填写服务端保存位置"
            class="!w-full sm:!w-[28rem] !font-mono"
          />
        </t-form-item>

        <t-form-item label="启动指令" name="args" class="w-full sm:!w-[40rem]">
          <template #help><span class="text-[11px] text-zinc-500 mt-1 inline-block">此模式不会自动帮您配置 Java 环境，您需要填写完整的启动命令。</span></template>
          <t-textarea
            v-model="formData.args"
            placeholder="例如: ./start.sh 或 java -jar server.jar..."
            :autosize="{ minRows: 5, maxRows: 25 }"
            class="code-font-textarea !bg-zinc-50/50 dark:!bg-zinc-900/30 !rounded-xl"
          />
        </t-form-item>

        <t-form-item label="忽略 EULA 提示" name="ignoreEula">
          <template #help><span class="text-[11px] text-zinc-500 mt-1 inline-block">若您的实例并非 MC 服务器，可打开此选项。</span></template>
          <div class="flex items-center gap-3">
            <t-switch v-model="formData.ignoreEula" size="large" />
            <span class="text-sm font-bold transition-colors" :class="formData.ignoreEula ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-secondary)]'">
              {{ formData.ignoreEula ? '已开启' : '已关闭' }}
            </span>
          </div>
        </t-form-item>

        <div class="mt-6 pt-6 border-t border-zinc-200 dark:border-zinc-700">
          <t-button theme="primary" type="submit" class="!rounded-xl !font-bold !h-11 !px-8 shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50">提交创建</t-button>
        </div>
      </t-form>
    </div>

    <div v-else class="flex flex-col items-center justify-center py-8 min-h-[50vh] sm:min-h-[40vh]">
      <t-icon name="check-circle" size="64px" class="text-[var(--color-success)]" />

      <div class="text-xl text-[var(--td-text-color-primary)] text-center font-medium leading-[22px] !mt-4">
        服务器 ({{ createdServerId }}) 已创建成功
      </div>

      <div class="text-sm text-[var(--td-text-color-secondary)] leading-[22px] !my-2 !mb-8">
        你现在可以去服务器列表启动它了
      </div>

      <div class="flex gap-4">
        <t-button @click="() => { goToHome(); changeUrl('/instance/list'); }">返回服务端列表</t-button>
        <t-button theme="default" @click="() => { goToHome(); changeUrl(`/instance/console/${createdServerId}`); }">前往控制台</t-button>
      </div>
    </div>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 进场动画 */
.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}
@keyframes slideUp {
  from { opacity: 0; transform: translateY(16px); }
  to { opacity: 1; transform: translateY(0); }
}


/* 等宽字体与代码 */
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre;
}
</style>
