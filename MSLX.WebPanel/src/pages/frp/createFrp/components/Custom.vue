<script setup lang="ts">
import { reactive, ref } from 'vue';
import { type FormProps, FormRules, MessagePlugin } from 'tdesign-vue-next';
import { convertIniToToml, createFrpTunnel } from '@/pages/frp/createFrp/utils/create';

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
    await createFrpTunnel(formData.name, formData.content, 'Custom', formData.type, false);
  } else {
    MessagePlugin.warning('请检查表单填写');
  }
};

const onReset = () => {
  MessagePlugin.info('表单已重置');
};
const handleConvertIniToToml = () => {
  try {
    formData.content = convertIniToToml(formData.content);
    formData.type = 'toml';
    MessagePlugin.success('已转换为 TOML');
  } catch (e) {
    MessagePlugin.error(`转换失败: ${e.message || '未知错误'}`);
  }
};
</script>
<template>
  <div class="custom-frp-container">
    <div
      class="design-card bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-6 sm:p-8"
    >
      <div class="flex items-center gap-2 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
        <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">自定义 Frp 隧道</h3>
      </div>

      <t-form ref="formRef" :data="formData" :rules="rules" label-align="top" @reset="onReset" @submit="onSubmit">
        <t-form-item label="隧道名称" name="name">
          <t-input v-model="formData.name" placeholder="请输入隧道名称" class="!w-full sm:!w-96" />
        </t-form-item>

        <t-form-item label="配置类型" name="type">
          <div class="flex items-center gap-4">
            <t-radio-group v-model="formData.type" variant="default-filled">
              <t-radio-button value="toml">TOML</t-radio-button>
              <t-radio-button value="ini">INI</t-radio-button>
            </t-radio-group>

            <transition name="fade">
              <t-button
                v-if="formData.type === 'ini'"
                variant="outline"
                theme="primary"
                size="small"
                class="!rounded-md hover:!bg-[var(--color-primary)]/10"
                @click="handleConvertIniToToml"
              >
                一键转 TOML
              </t-button>
            </transition>
          </div>
        </t-form-item>

        <t-form-item label="隧道配置内容" name="content">
          <t-textarea
            v-model="formData.content"
            placeholder='serverAddr = "0.0.0.0"&#10;serverPort = 1027&#10;&#10;[[proxies]]&#10;name = "nahida_tcp"&#10;...'
            :autosize="{ minRows: 12, maxRows: 25 }"
            class="code-font-textarea !bg-zinc-50/50 dark:!bg-zinc-900/50 !w-full"
          />
        </t-form-item>

        <div
          class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center gap-3"
        >
          <t-button
            theme="primary"
            type="submit"
            class="!rounded-xl !font-bold !px-8 shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50"
            >保存配置</t-button
          >
          <t-button
            theme="default"
            variant="base"
            type="reset"
            class="!bg-zinc-100 dark:!bg-zinc-800/80 !border-none !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 !rounded-xl !font-bold"
            >重置</t-button
          >
        </div>
      </t-form>
    </div>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 针对代码编辑框的特殊优化 */
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre;
  @apply !text-zinc-800 dark:!text-zinc-300;
}

.fade-enter-active,
.fade-leave-active {
  transition:
    opacity 0.3s ease,
    transform 0.3s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateX(-10px);
}
</style>
