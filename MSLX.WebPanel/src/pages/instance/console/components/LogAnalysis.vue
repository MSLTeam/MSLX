<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import { AppIcon, AnalyticsFilledIcon, BrowseIcon, ExtensionIcon, FileIcon, TimeIcon } from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { getInstanceInfo } from '@/api/instance';
import { getFileContent, getPluginsOrModsList } from '@/api/files';
import { getAIServiceModelsList, getAIServiceUsage, postAIAnalysis } from '@/api/msl-user/aiLogAnalysis';
import { changeUrl } from '@/router';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from '@vueuse/core';
import { formatTime } from '@/utils/tools';
import { ModelInfoModel } from '@/api/msl-user/model/aiLogAnalysis';
import { useRoute } from 'vue-router';

const route = useRoute();

// 定义 Props
const props = defineProps<{
  visible: boolean; // 控制弹窗显示
  serverId: number; // 父组件传入的 ID 参数
}>();

// 定义 Emits
const emit = defineEmits(['update:visible', 'submit']);

// 表单数据模型
const formData = reactive({
  coreVersion: '',
  envType: 'mods',
  modsList: '',
  pluginsList: '',
  logContent: '',
  result: '> ✨ 等待日志分析开始······',
  selectedModel: 'Qwen/Qwen3-14B',
});
const modelList = ref<ModelInfoModel[]>([]);

// 统计数据
const stats = reactive({
  max: 0,
  today: 0,
  extra: 0,
  lastTime: 0,
});

// 状态控制
const dialogVisible = ref(false);
const loading = ref(false);
const analysing = ref(false);

async function getUsage() {
  const res_usage = await getAIServiceUsage(localStorage.getItem('msl-user-token'));
  if (res_usage.code === 200) {
    stats.max = res_usage.data.max_per_day;
    stats.extra = res_usage.data.extra_tokens;
    stats.lastTime = res_usage.data.last_use_time;
    stats.today = res_usage.data.today_usage;
  } else {
    MessagePlugin.error('MSL账号未登录或已失效，请重新登录！');
    changeUrl('/frp/create');
    return;
  }
}

async function initInstanceInfo() {
  try {
    loading.value = true;
    // 获取用量信息
    await getUsage();
    // 模型列表
    try {
      const res_models = await getAIServiceModelsList(localStorage.getItem('msl-user-token') || '');
      if (res_models.code === 200) {
        modelList.value = res_models.data;
        if (!formData.selectedModel && modelList.value.length > 0) {
          formData.selectedModel = modelList.value[0].name;
        }
      }
    } catch (e) {
      MessagePlugin.error('获取模型列表失败' + e.message);
    }
    // 获取服务端名字
    const res_info = await getInstanceInfo(props.serverId);
    formData.coreVersion = res_info.core;
    // 获取模组、插件列表
    try {
      const res_mods = await getPluginsOrModsList(props.serverId, 'mods', false);
      formData.modsList = (res_mods.jarFiles || []).join('\n');
      formData.envType = 'mods';
    } catch {
      formData.modsList = '';
    }
    try {
      const res_plugins = await getPluginsOrModsList(props.serverId, 'plugins', false);
      formData.pluginsList = (res_plugins.jarFiles || []).join('\n');
      if (formData.modsList == '') {
        formData.envType = 'plugins';
      }
    } catch {
      formData.pluginsList = '';
    }
    // 获取最新日志
    try {
      formData.logContent = await getFileContent(props.serverId, 'logs/latest.log');
    } catch {
      /* empty */
    }
  } catch (e) {
    MessagePlugin.error(e.message);
  }
  loading.value = false;
}

// 监听外部 visible 变化
watch(
  () => props.visible,
  (val) => {
    if (route.name !== 'InstanceConsole') {
      return;
    }
    dialogVisible.value = val;
    if (val) {
      initInstanceInfo();
    }
  },
);

// 监听内部关闭事件
const onClose = () => {
  emit('update:visible', false);
};

// 提交 AI 诊断
async function handleAnalyze() {
  if (!formData.logContent) return;
  if (!formData.selectedModel) {
    MessagePlugin.warning('请先选择分析模型');
    return;
  }
  analysing.value = true;
  formData.result = `> ⌛️ 分析日志中··· 请稍等`;
  try {
    const res = await postAIAnalysis(
      localStorage.getItem('msl-user-token'),
      formData.modsList,
      formData.pluginsList,
      formData.coreVersion,
      formData.logContent,
      formData.selectedModel,
    );
    formData.result = res.data.content;
  } catch (e) {
    formData.result = `> ❌ 分析出现错误: ${e.message}`;
  }
  analysing.value = false;
  await getUsage();
}

// md暗黑模式逻辑
const isDark = useDark();
const mdTheme = ref(isDark.value ? 'dark' : 'light');
watch(isDark, () => {
  mdTheme.value = isDark.value ? 'dark' : 'light';
});
</script>

<template>
  <t-dialog
    v-model:visible="dialogVisible"
    width="90%"
    top="3vh"
    :footer="false"
    :close-on-overlay-click="false"
    class="log-analysis-dialog"
    @close="onClose"
    attach="body"
  >
    <template #header>
      <div class="flex items-center gap-2 font-bold text-lg text-zinc-800 dark:text-zinc-200">
        AI 错误日志分析
      </div>
    </template>

    <t-loading :loading="loading">
      <div class="flex flex-col md:flex-row w-full h-[75vh] md:h-[70vh] rounded-xl overflow-hidden bg-white/80 dark:bg-zinc-800/80 border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm">

        <div class="list-item-anim w-full md:w-[40%] min-w-[320px] p-5 flex flex-col gap-5 border-b md:border-b-0 md:border-r border-zinc-200/60 dark:border-zinc-700/60 overflow-y-auto custom-scrollbar" style="animation-delay: 0s;">

          <div class="flex flex-col gap-2">
            <div class="text-[11px] font-bold text-zinc-500 dark:text-zinc-400 uppercase tracking-wider">选择分析模型</div>
            <t-select v-model="formData.selectedModel" placeholder="请选择 AI 模型" filterable class="!w-full">
              <t-option v-for="item in modelList" :key="item.name" :value="item.name" :label="item.name">
                <div class="flex justify-between items-center w-full gap-2">
                  <span class="font-bold">{{ item.name }}</span>
                  <t-tag size="small" variant="light" class="!rounded">倍率: {{ item.rate }}x</t-tag>
                </div>
              </t-option>
            </t-select>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-[11px] font-bold text-zinc-500 dark:text-zinc-400 uppercase tracking-wider">服务端核心 / 版本</div>
            <t-input v-model="formData.coreVersion" readonly placeholder="例如: Arclight 1.21.1">
              <template #prefix-icon><app-icon class="text-zinc-400" /></template>
            </t-input>
          </div>

          <div class="flex flex-col gap-2">
            <div class="text-[11px] font-bold text-zinc-500 dark:text-zinc-400 uppercase tracking-wider">环境列表</div>

            <t-radio-group v-model="formData.envType" variant="default-filled" class="flex w-full">
              <t-radio-button value="mods" class="flex-1 !text-center">
                <div class="flex justify-center items-center gap-1.5"><app-icon size="14px" /> 模组 (Mods)</div>
              </t-radio-button>
              <t-radio-button value="plugins" class="flex-1 !text-center">
                <div class="flex justify-center items-center gap-1.5"><extension-icon size="14px" /> 插件 (Plugins)</div>
              </t-radio-button>
            </t-radio-group>

            <t-textarea
              v-show="formData.envType === 'mods'"
              v-model="formData.modsList"
              readonly
              :autosize="{ minRows: 4, maxRows: 6 }"
              placeholder="暂无模组数据..."
              class="mt-1 !bg-zinc-50 dark:!bg-zinc-900/30 !text-zinc-500 font-mono text-xs"
            />

            <t-textarea
              v-show="formData.envType === 'plugins'"
              v-model="formData.pluginsList"
              readonly
              :autosize="{ minRows: 4, maxRows: 6 }"
              placeholder="暂无插件数据..."
              class="mt-1 !bg-zinc-50 dark:!bg-zinc-900/30 !text-zinc-500 font-mono text-xs"
            />
          </div>

          <div class="flex flex-col gap-2 flex-1 min-h-[150px]">
            <div class="text-[11px] font-bold text-zinc-500 dark:text-zinc-400 uppercase tracking-wider">错误日志内容</div>
            <t-textarea
              v-model="formData.logContent"
              placeholder="没找到有效日志，您可以手动粘贴日志......"
              class="flex-1 [&_textarea]:!h-full [&_textarea]:!resize-none font-mono text-xs"
            />
          </div>

          <t-button :loading="analysing" block theme="primary" size="large" class="!rounded-xl !h-12 !font-bold shadow-sm shrink-0 mt-2" @click="handleAnalyze">
            <template #icon><analytics-filled-icon /></template> 开始 AI 诊断
          </t-button>
        </div>

        <div class="list-item-anim flex-1 flex flex-col bg-zinc-50/50 dark:bg-zinc-900/30 overflow-hidden" style="animation-delay: 0.1s;">

          <div class="px-5 py-4 flex flex-wrap justify-between items-center gap-3 border-b border-zinc-200/60 dark:border-zinc-700/60 bg-white/50 dark:bg-zinc-800/30">
            <span class="text-sm font-bold text-zinc-800 dark:text-zinc-200">AI 分析报告</span>
            <div class="flex flex-wrap items-center gap-2">
              <t-tag theme="default" variant="light" class="!rounded font-bold">
                <template #icon><browse-icon /></template> 今日: {{ stats.today }} / {{ stats.max }}
              </t-tag>
              <t-tag theme="primary" variant="light" class="!rounded font-bold">
                <template #icon><file-icon /></template> 额外: {{ stats.extra }}
              </t-tag>
              <t-tag theme="warning" variant="light" class="!rounded font-bold">
                <template #icon><time-icon /></template> 上次: {{ formatTime(stats.lastTime) }}
              </t-tag>
            </div>
          </div>

          <div class="flex-1 overflow-y-auto custom-scrollbar p-5">
            <md-preview
              editor-id="report-preview"
              :model-value="formData.result"
              :theme="mdTheme as Themes"
              class="md-preview-wrapper !bg-transparent"
            />
          </div>

        </div>
      </div>
    </t-loading>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

/* === 阶梯滑入动画 === */
.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
}

.custom-scrollbar {
  .scrollbar-mixin();
}


/* === Markdown 编辑器深度定制 === */
:deep(.md-editor-preview a) {
  color: var(--td-brand-color);
  text-decoration: none;
  font-weight: 500;
  &:hover { text-decoration: underline; }
}

:deep(.md-editor-preview code) {
  color: var(--td-brand-color);
  background-color: color-mix(in srgb, var(--td-brand-color), transparent 90%);
  border-radius: 4px;
  padding: 2px 4px;
  font-family: monospace;
}

:deep(.md-editor div.default-theme) {
  --md-theme-quote-border: 4px solid var(--td-brand-color);
}

:deep(.md-editor-preview) {
  --md-color: var(--td-text-color-primary) !important;
  --md-bk-color: transparent;
}
</style>
