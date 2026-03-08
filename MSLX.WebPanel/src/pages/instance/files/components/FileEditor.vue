<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted } from 'vue';
import { Codemirror } from 'vue-codemirror';
import { oneDark } from '@codemirror/theme-one-dark';
import { EditorState } from '@codemirror/state';
import { json } from '@codemirror/lang-json';
import { yaml } from '@codemirror/lang-yaml';
import { javascript } from '@codemirror/lang-javascript';
import { css } from '@codemirror/lang-css';
import { html } from '@codemirror/lang-html';
import { StreamLanguage } from '@codemirror/language';
import { toml } from '@codemirror/legacy-modes/mode/toml';

// 代码格式化
import prettier from 'prettier/standalone';
import parserYaml from 'prettier/plugins/yaml';
import parserBabel from 'prettier/plugins/babel'; //  JS/JSON
import parserEstree from 'prettier/plugins/estree'; // Babel 的依赖
import parserHtml from 'prettier/plugins/html';
import parserPostcss from 'prettier/plugins/postcss';
import { MessagePlugin } from 'tdesign-vue-next'; // 用于 CSS

const props = defineProps({
  visible: Boolean,
  fileName: String,
  content: String,
  loading: Boolean,
});

const emit = defineEmits(['update:visible', 'save']);

// 处理代码格式化
const handleFormat = async () => {
  if (!code.value) return;

  // 获取文件后缀
  const ext = props.fileName?.split('.').pop()?.toLowerCase();

  let parserName = '';
  let plugins = [];

  switch (ext) {
    case 'json':
      parserName = 'json';
      plugins = [parserBabel, parserEstree];
      break;
    case 'yml':
    case 'yaml':
      parserName = 'yaml';
      plugins = [parserYaml];
      break;
    case 'js':
    case 'ts':
      parserName = 'babel';
      plugins = [parserBabel, parserEstree];
      break;
    case 'css':
    case 'less':
    case 'scss':
      parserName = 'css';
      plugins = [parserPostcss];
      break;
    case 'html':
    case 'xml':
      parserName = 'html';
      plugins = [parserHtml];
      break;
    default:
      MessagePlugin.warning('该文件类型暂不支持自动格式化');
      return;
  }

  try {
    // Prettier 格式化
    const formatted = await prettier.format(code.value, {
      parser: parserName,
      plugins: plugins,
      tabWidth: 2,
      printWidth: 80,
      semi: true,
      singleQuote: true,
    });

    code.value = formatted;
    MessagePlugin.success('格式化成功');
  } catch (error: any) {
    console.error(error);
    // 语法错误
    MessagePlugin.error(`格式化失败: 请检查语法错误\n${error.message.split('\n')[0]}`);
  }
};

const code = ref('');
const isDarkMode = ref(false); // 用于存储当前是否为暗黑模式

// 主题变化监听
let observer: MutationObserver | null = null;

const checkTheme = () => {
  const mode = document.documentElement.getAttribute('theme-mode');
  isDarkMode.value = mode === 'dark';
};

onMounted(() => {
  checkTheme(); // 初始化检查
  // 监听 html 标签属性变化
  observer = new MutationObserver(checkTheme);
  observer.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['theme-mode'],
  });
});

onUnmounted(() => {
  observer?.disconnect();
});

// --- 初始化内容 ---
watch(
  () => props.content,
  (newVal) => {
    code.value = newVal || '';
  },
  { immediate: true },
);

// --- 动态计算编辑器扩展 ---
const extensions = computed(() => {
  const result = [];

  if (isDarkMode.value) {
    result.push(oneDark);
  }

  // 汉化配置
  result.push(
    EditorState.phrases.of({
      // 搜索框占位符
      Find: '查找内容...',
      Replace: '替换为...',

      // 按钮/选项
      next: '下一个',
      previous: '上一个',
      all: '选中所有',
      'match case': '区分大小写',
      'by word': '全字匹配',
      regexp: '正则表达式',
      replace: '替换',
      'replace all': '替换全部',
      close: '关闭',
    }),
  );

  // 匹配语言
  const ext = props.fileName?.split('.').pop()?.toLowerCase();
  switch (ext) {
    case 'json':
      result.push(json());
      break;
    case 'yml':
    case 'yaml':
      result.push(yaml());
      break;
    case 'js':
    case 'ts':
      result.push(javascript());
      break;
    case 'css':
    case 'less':
    case 'scss':
      result.push(css());
      break;
    case 'html':
    case 'xml':
      result.push(html());
      break;
    case 'toml':
    case 'ini':
    case 'conf':
      result.push(StreamLanguage.define(toml));
      break;
    case 'properties':
    case 'log':
    case 'txt':
    default:
      break;
  }

  return result;
});

const handleClose = () => {
  emit('update:visible', false);
};

const handleConfirm = () => {
  emit('save', code.value);
};
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="`正在编辑: ${fileName}`"
    width="90%"
    attach="body"
    top="2vh"
    class="editor-dialog"
    @close="handleClose"
  >
    <div class="flex flex-col gap-2">
      <div class="border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl overflow-hidden shadow-inner bg-white dark:bg-zinc-900/30">
        <codemirror
          v-model="code"
          placeholder="文件内容为空..."
          :style="{ height: '60vh', fontSize: '14px' }"
          :autofocus="true"
          :indent-with-tab="true"
          :tab-size="2"
          :extensions="extensions"
        />
      </div>

      <div class="flex justify-end items-center gap-4 px-1 text-[11.5px] font-mono text-zinc-400 dark:text-zinc-500 tracking-wider">
        <span>行数: {{ code.split('\n').length }}</span>
        <span>长度: {{ code.length }}</span>
        <span class="text-[var(--color-primary)] font-medium bg-[var(--color-primary)]/10 px-1.5 py-0.5 rounded">
          {{ isDarkMode ? 'Dark Mode' : 'Light Mode' }}
        </span>
      </div>
    </div>

    <template #footer>
      <div class="flex justify-between items-center w-full mt-2">
        <div class="flex">
          <t-button variant="outline" theme="default" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleFormat">
            自动格式化
          </t-button>
        </div>

        <div class="flex items-center gap-2">
          <t-button variant="outline" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose">
            取消
          </t-button>
          <t-button theme="primary" class="!rounded-lg shadow-sm" :loading="props.loading" @click="handleConfirm">
            保存
          </t-button>
        </div>
      </div>
    </template>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

:deep(.t-dialog) {
  max-width: 95vw !important;
}

:deep(.cm-editor) {
  font-family: 'Maple Mono', 'Maple Mono CN', 'Cascadia Code', Consolas, Menlo, 'PingFang SC', 'Microsoft YaHei', monospace !important;
  font-variant-ligatures: common-ligatures;

  &.cm-focused {
    outline: none !important;
  }
}

:deep(.cm-scroller),
:deep(.cm-gutters) {
  font-family: inherit !important;
}
</style>
