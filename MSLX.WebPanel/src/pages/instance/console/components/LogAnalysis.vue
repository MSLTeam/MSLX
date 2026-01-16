<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import { AppIcon, AnalyticsFilledIcon, BrowseIcon, ExtensionIcon, FileIcon, TimeIcon } from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { getInstanceInfo } from '@/api/instance';
import { getFileContent, getPluginsOrModsList } from '@/api/files';
import { getAIServiceUsage, postAIAnalysis } from '@/api/msl-user/aiLogAnalysis';
import { changeUrl } from '@/router';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from '@vueuse/core';
import { formatTime } from '@/utils/tools';

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
});

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
  analysing.value = true;
  formData.result = `> ⌛️ 分析日志中··· 请稍等`;
  try {
    const res = await postAIAnalysis(
      localStorage.getItem('msl-user-token'),
      formData.modsList,
      formData.pluginsList,
      formData.coreVersion,
      formData.logContent,
      'qwen-flash',
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
  >
    <template #header>
      <div class="dialog-title">
        <span style="display: flex; align-items: center; gap: 8px"> AI 错误日志分析 </span>
      </div>
    </template>
    <t-loading :loading="loading">
      <div class="analysis-container">
        <div class="panel-left">
          <div class="form-group">
            <div class="label">服务端核心/版本</div>
            <t-input v-model="formData.coreVersion" readonly placeholder="例如: Arclight 1.21.1">
              <template #prefix-icon>
                <app-icon />
              </template>
            </t-input>
          </div>

          <div class="form-group">
            <div class="label">环境列表</div>

            <div class="env-tabs">
              <t-radio-group v-model="formData.envType" variant="default-filled" class="custom-radio-group">
                <t-radio-button value="mods">
                  <template #default> <app-icon class="mr-1" /> 模组 (Mods) </template>
                </t-radio-button>
                <t-radio-button value="plugins">
                  <template #default> <extension-icon class="mr-1" /> 插件 (Plugins) </template>
                </t-radio-button>
              </t-radio-group>
            </div>

            <t-textarea
              v-show="formData.envType === 'mods'"
              v-model="formData.modsList"
              readonly
              :autosize="{ minRows: 4, maxRows: 6 }"
              placeholder="暂无模组数据..."
              class="mt-2 readonly-textarea"
            />

            <t-textarea
              v-show="formData.envType === 'plugins'"
              v-model="formData.pluginsList"
              readonly
              :autosize="{ minRows: 4, maxRows: 6 }"
              placeholder="暂无插件数据..."
              class="mt-2 readonly-textarea"
            />
          </div>

          <div class="form-group flex-grow">
            <div class="label">错误日志内容</div>
            <t-textarea
              v-model="formData.logContent"
              placeholder="没找到有效日志，您可以手动粘贴日志......"
              class="log-textarea"
            />
          </div>

          <div class="action-bar">
            <t-button :loading="analysing" block theme="primary" size="large" @click="handleAnalyze">
              <template #icon><analytics-filled-icon /></template>
              开始 AI 诊断
            </t-button>
          </div>
        </div>

        <div class="panel-right">
          <div class="panel-header">
            <span class="title">AI 分析报告</span>
            <div class="stats-tags">
              <t-tag theme="default" variant="light" size="small">
                <template #icon><browse-icon /></template>
                今日: {{ stats.today }} / {{ stats.max }}
              </t-tag>
              <t-tag theme="primary" variant="light" size="small">
                <template #icon><file-icon /></template>
                额外: {{ stats.extra }}
              </t-tag>
              <t-tag theme="warning" variant="light" size="small">
                <template #icon><time-icon /></template>
                上次: {{ formatTime(stats.lastTime) }}
              </t-tag>
            </div>
          </div>

          <div class="report-content">
            <md-preview
              editor-id="report-preview"
              :model-value="formData.result"
              :theme="mdTheme as Themes"
              class="md-preview-wrapper"
            />
          </div>
        </div>
      </div>
    </t-loading>
  </t-dialog>
</template>

<style scoped lang="less">
/* 工具类 */
.mr-1 {
  margin-right: 4px;
}
.mt-2 {
  margin-top: 8px;
}

.analysis-container {
  display: flex;
  width: 100%;
  height: 70vh;
  background-color: var(--td-bg-color-container); /* 适配暗黑模式背景 */

  /* 移动端适配 */
  @media (max-width: 768px) {
    flex-direction: column;
    overflow-y: auto;
  }
}

/* 左侧面板 */
.panel-left {
  width: 40%; /* 桌面端宽度 */
  min-width: 350px;
  padding: 24px;
  border-right: 1px solid var(--td-component-border); /* 适配暗黑模式边框 */
  display: flex;
  flex-direction: column;
  gap: 20px;
  background-color: var(--td-bg-color-container);

  @media (max-width: 768px) {
    width: 100%;
    border-right: none;
    border-bottom: 1px solid var(--td-component-border);
    min-width: unset;
  }
}

/* 右侧面板 */
.panel-right {
  flex: 1;
  display: flex;
  flex-direction: column;
  background-color: var(--td-bg-color-secondarycontainer);
  overflow: hidden;

  @media (max-width: 768px) {
    min-height: 400px;
  }
}

/* 表单组通用样式 */
.form-group {
  .label {
    font-size: 14px;
    font-weight: 500;
    color: var(--td-text-color-primary);
    margin-bottom: 8px;
    text-align: center; /* 图片中标题是居中的 */
  }

  &.flex-grow {
    flex: 1;
    display: flex;
    flex-direction: column;

    /* 让文本域充满剩余空间 */
    :deep(.t-textarea),
    :deep(.t-textarea__inner) {
      height: 100%;
      resize: none;
    }
  }
}

/* 环境列表 Tabs 样式微调 */
.env-tabs {
  background: var(--td-bg-color-secondarycontainer);
  padding: 4px;
  border-radius: var(--td-radius-default);

  :deep(.t-radio-group) {
    width: 100%;
    display: flex;
  }

  :deep(.t-radio-button) {
    flex: 1;
    text-align: center;
  }
}

/* 只读文本域 */
.readonly-textarea {
  :deep(.t-textarea__inner) {
    color: var(--td-text-color-secondary);
    background-color: var(--td-bg-color-component-disabled);
    cursor: default;
  }
}

/* 右侧头部 */
.panel-header {
  padding: 16px 24px;
  border-bottom: 1px solid var(--td-component-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--td-bg-color-container);

  /* 内容过多时自动换行 */
  flex-wrap: wrap;
  gap: 12px; /* 换行后的行间距 */

  .title {
    font-size: 16px;
    font-weight: 700;
    color: var(--td-text-color-primary);

    /* 防止出现竖排文字 */
    white-space: nowrap;
    flex-shrink: 0;
  }

  .stats-tags {
    display: flex;
    gap: 8px;
    align-items: center;

    margin-left: auto;

    @media (max-width: 500px) {
      flex-wrap: wrap;
      justify-content: flex-end;
      width: 100%;
    }
  }

  /* 移动端 */
  @media (max-width: 500px) {
    align-items: flex-start; /* 左对齐 */

    .stats-tags {
      margin-left: 0; /* 取消靠右，改为自然流式布局 */
      justify-content: flex-start;
      margin-top: 4px;
    }
  }
}

.report-content {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
  position: relative;
}

// md内容

.md-preview-wrapper {
  background: none;
  padding: 10px;
}

// 覆盖MD编辑器链接颜色
:deep(.md-editor-preview a) {
  color: var(--td-brand-color);
  text-decoration: none;
  &:hover {
    text-decoration: underline;
  }
}

// 覆盖代码块颜色
:deep(.md-editor-preview code) {
  color: var(--td-brand-color);
  background-color: color-mix(in srgb, var(--td-brand-color), transparent 90%);
  border-radius: 4px;
  padding: 2px 4px;
}

// 引用块左边框颜色
:deep(.md-editor div.default-theme) {
  --md-theme-quote-border: 4px solid var(--td-brand-color);
}

// 暗黑模式适配
:deep(.md-editor-dark) {
  --md-color: var(--td-text-color-primary);
  --md-bk-color: transparent;
}

// 亮色模式适配
:deep(.md-editor-light) {
  --md-color: var(--td-text-color-primary);
  --md-bk-color: transparent; // 设为透明
}
</style>
