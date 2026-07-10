<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
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
  dockerImage: 'MSLX://DockerImage/Java/25',
  dockerPorts: '25565:25565',
});

const rules: FormRules<CreateInstanceQucikModeModel> = {
  name: [{ required: true, message: '请输入服务器名称', type: 'error' }],
  args: [{ required: true, message: '请输入自定义启动参数', type: 'error' }],
};

// docker
const isDockerActive = ref(false);
const dockerImageType = ref('preset');
const dockerImagePresetVersion = ref('25');

watch([isDockerActive, dockerImageType, dockerImagePresetVersion], ([active, type, ver]) => {
  if (active) {
    formData.java = 'docker-custom';
    if (type === 'preset') {
      formData.dockerImage = `MSLX://DockerImage/Java/${ver}`;
    }
  } else {
    formData.java = 'none'; // 退回普通自由进程模式
  }
});

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
  isDockerActive.value = false;
  dockerImageType.value = 'preset';
  dockerImagePresetVersion.value = '25';

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
    dockerImage: 'MSLX://DockerImage/Java/25',
    dockerPorts: '25565:25565',
  });
};
</script>

<template>
  <div
    class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-sm p-6 sm:p-8"
  >
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
          :help="
            userStore.userInfo.systemInfo.docker
              ? '您的MSLX正在运行于Docker环境内，为保数据安全，仅支持使用默认数据路径'
              : '选填，留空将使用默认路径'
          "
        >
          <t-input
            v-model="formData.path"
            :disabled="userStore.userInfo.systemInfo.docker"
            placeholder="请填写服务端保存位置"
            class="!w-full sm:!w-[28rem] !font-mono"
          />
        </t-form-item>

        <t-form-item label="Docker 容器沙盒隔离" name="dockerToggle">
          <template #help
            ><span class="text-[11px] text-zinc-500 mt-1 inline-block"
              >开启后，您的自定义命令将在独立的 Docker 容器内拉起。</span
            ></template
          >
          <div class="flex items-center gap-3">
            <t-switch v-model="isDockerActive" size="large" />
            <span
              class="text-sm font-bold transition-colors"
              :class="isDockerActive ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-secondary)]'"
            >
              {{ isDockerActive ? '已启用沙盒运行' : '已关闭' }}
            </span>
          </div>
        </t-form-item>

        <div
          v-if="isDockerActive"
          class="list-item-anim border border-zinc-200 dark:border-zinc-800 rounded-2xl p-4 bg-zinc-50/30 dark:bg-zinc-900/10 w-full sm:w-[32rem] !mb-6"
        >
          <div class="mb-4">
            <t-radio-group v-model="dockerImageType" size="small" variant="outline">
              <t-radio-button value="preset">使用 MSLX 官方运行时环境</t-radio-button>
              <t-radio-button value="custom">自定义镜像</t-radio-button>
            </t-radio-group>
          </div>

          <div v-if="dockerImageType === 'preset'">
            <div class="flex items-center gap-3">
              <span class="text-xs font-bold text-zinc-500 shrink-0">内部预设环境:</span>
              <t-select v-model="dockerImagePresetVersion" class="!w-48" :clearable="false">
                <t-option label="Java 25" value="25" />
                <t-option label="Java 21" value="21" />
                <t-option label="Java 17" value="17" />
                <t-option label="Java 11" value="11" />
                <t-option label="Java 8" value="8" />
              </t-select>
            </div>
          </div>

          <div v-if="dockerImageType === 'custom'">
            <t-input
              v-model="formData.dockerImage"
              placeholder="例如: ubuntu:latest 或 python:3.10-slim"
              class="!font-mono"
            />
            <div class="text-[11px] text-amber-500 mt-2">
              ⚠️ 提示：使用自定义公共镜像创建时，系统将通过终端执行标准 docker
              pull，请确保网络通畅（如有需要请添加镜像源前缀）。
            </div>
          </div>

          <div class="mt-4 pt-4 border-t border-zinc-200/60 dark:border-zinc-700/50 flex flex-col gap-2">
            <span class="text-xs font-bold text-[var(--td-text-color-primary)]">基础映射端口放行</span>
            <t-input
              v-model="formData.dockerPorts"
              placeholder="例如 25565:25565，留空或0代表使用Host模式"
              class="!font-mono"
            />
          </div>
        </div>

        <t-form-item label="启动指令" name="args" class="w-full sm:!w-[40rem]">
          <t-textarea
            v-model="formData.args"
            placeholder="例如: ./start.sh 或 java -jar server.jar..."
            :autosize="{ minRows: 5, maxRows: 25 }"
            class="code-font-textarea !bg-zinc-50/50 dark:!bg-zinc-900/30 !rounded-xl"
          />
        </t-form-item>

        <t-form-item label="忽略 EULA 提示" name="ignoreEula">
          <template #help
            ><span class="text-[11px] text-zinc-500 mt-1 inline-block"
              >若您的实例并非 MC 服务器，可打开此选项。</span
            ></template
          >
          <div class="flex items-center gap-3">
            <t-switch v-model="formData.ignoreEula" size="large" />
            <span
              class="text-sm font-bold transition-colors"
              :class="formData.ignoreEula ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-secondary)]'"
            >
              {{ formData.ignoreEula ? '已开启' : '已关闭' }}
            </span>
          </div>
        </t-form-item>

        <div class="mt-6 pt-6 border-t border-zinc-200 dark:border-zinc-700">
          <t-button
            theme="primary"
            type="submit"
            class="!rounded-xl !font-bold !h-11 !px-8 shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50"
            >提交创建</t-button
          >
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
        <t-button
          @click="
            () => {
              goToHome();
              changeUrl('/instance/list');
            }
          "
          >返回服务端列表</t-button
        >
        <t-button
          theme="default"
          @click="
            () => {
              goToHome();
              changeUrl(`/instance/console/${createdServerId}`);
            }
          "
          >前往控制台</t-button
        >
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
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* 等宽字体与代码 */
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre;
}
</style>
