<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { RefreshIcon, ChatIcon } from 'tdesign-icons-vue-next';
import { getAiSettings, updateAiSettings, AiSettingsModel } from '@/api/aiApi';

const loading = ref(false);
const submitLoading = ref(false);

const formData = ref<AiSettingsModel>({
  aiEnabled: false,
  aiApiKey: '',
  aiBaseUrl: 'https://api.deepseek.com/v1',
  aiModelName: 'deepseek-chat',
});

const emit = defineEmits(['refresh']);

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getAiSettings();
    if (res) {
      formData.value = {
        aiEnabled: res.aiEnabled ?? false,
        aiApiKey: res.aiApiKey ?? '',
        aiBaseUrl: res.aiBaseUrl || 'https://api.deepseek.com/v1',
        aiModelName: res.aiModelName || 'deepseek-chat',
      };
    }
  } catch (e: any) {
    MessagePlugin.error(`加载 AI 配置失败: ${e.message || e}`);
  } finally {
    loading.value = false;
  }
};

const handleSave = async () => {
  submitLoading.value = true;
  try {
    await updateAiSettings(formData.value);
    MessagePlugin.success('AI 助手配置保存成功');
    await loadData();
  } catch (e: any) {
    MessagePlugin.error(`保存失败: ${e.message || e}`);
  } finally {
    submitLoading.value = false;
  }
};

const handleRefresh = () => {
  loadData();
  emit('refresh');
};

defineExpose({ initData: loadData });

onMounted(() => {
  loadData();
});
</script>

<template>
  <div
    class="design-card relative flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm transition-all duration-300"
  >
    <t-loading :loading="loading" show-overlay>
      <div class="p-5 sm:p-6 sm:px-8">
        <div
          class="flex items-center justify-between mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60"
        >
          <div class="flex items-center gap-3">
            <div
              class="w-1.5 h-5 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"
            ></div>
            <div class="flex flex-col">
              <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">
                AI 智能运维助手
              </h2>
              <span class="text-[11px] sm:text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">
                配置大模型 API 密钥以开启自然语言建服与运维功能（支持 DeepSeek / Qwen / OpenAI 兼容格式）。
              </span>
            </div>
          </div>
          <t-button variant="dashed" size="small" class="!bg-transparent" @click="handleRefresh">
            <template #icon><refresh-icon /></template>
            刷新数据
          </t-button>
        </div>

        <t-form :data="formData" :label-width="140" label-align="left" @submit="handleSave">
          <div class="flex items-center gap-3 mt-2 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
              >基础开关</span
            >
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <t-form-item label="启用 AI 助手">
            <template #help>
              <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                开启后，系统将在全局界面右上角展示悬浮的 AI 对话入口。
              </span>
            </template>
            <div class="flex items-center gap-3">
              <t-switch v-model="formData.aiEnabled" />
              <span
                class="text-[11px] font-extrabold px-2 py-0.5 rounded-md transition-colors"
                :class="
                  formData.aiEnabled
                    ? 'bg-[var(--color-primary)]/10 text-[var(--color-primary)] border border-[var(--color-primary)]/20'
                    : 'bg-zinc-100 dark:bg-zinc-800 text-zinc-500 border border-zinc-200 dark:border-zinc-700'
                "
              >
                {{ formData.aiEnabled ? 'AI 助手已激活' : '已关闭' }}
              </span>
            </div>
          </t-form-item>

          <template v-if="formData.aiEnabled">
            <div class="flex items-center gap-3 mt-8 mb-6">
              <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest"
                >模型配置</span
              >
              <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
            </div>

            <t-form-item label="API Base URL">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                  请填写包含 <code>/v1</code> 的完整地址，例如 <code>https://api.deepseek.com/v1</code> 或 <code>https://dashscope.aliyuncs.com/compatible-mode/v1</code>。
                </span>
              </template>
              <t-input
                v-model="formData.aiBaseUrl"
                placeholder="https://api.deepseek.com/v1"
                class="!w-full sm:!w-[400px] !font-mono !text-sm"
              />
            </t-form-item>

            <t-form-item label="API Key">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                  大模型的 API 访问密钥，通常以 <code>sk-</code> 开头。
                </span>
              </template>
              <t-input
                v-model="formData.aiApiKey"
                type="password"
                placeholder="请输入大模型的 API Key (例如 sk-xxxxx)"
                class="!w-full sm:!w-[400px] !font-mono !text-sm"
              />
            </t-form-item>

            <t-form-item label="模型名称">
              <template #help>
                <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
                  指定模型标识，例如 <code>deepseek-chat</code>、<code>qwen-max</code>、<code>gpt-4o-mini</code> 等。
                </span>
              </template>
              <t-input
                v-model="formData.aiModelName"
                placeholder="如 deepseek-chat, qwen-max, gpt-4o-mini"
                class="!w-full sm:!w-[400px] !font-mono !text-sm"
              />
            </t-form-item>
          </template>

          <div
            class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center justify-between"
          >
            <t-button
              theme="primary"
              type="submit"
              :loading="submitLoading"
              class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow"
            >
              保存 AI 配置
            </t-button>

            <span class="text-[11px] text-zinc-400 dark:text-zinc-500 hidden sm:flex items-center gap-1">
              <chat-icon /> 配置生效后即刻可用
            </span>
          </div>
        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
