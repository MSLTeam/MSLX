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
    case 'properties':
    case 'conf':
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
    top="1vh"
    class="editor-dialog"
    @close="handleClose"
  >
    <div class="editor-container">
      <codemirror
        v-model="code"
        placeholder="文件内容为空..."
        :style="{ height: '70vh', fontSize: '14px' }"
        :autofocus="true"
        :indent-with-tab="true"
        :tab-size="2"
        :extensions="extensions"
      />
    </div>

    <div class="editor-status-bar">
      <span>行数: {{ code.split('\n').length }}</span>
      <span>长度: {{ code.length }}</span>
      <span class="mode-tag">{{ isDarkMode ? 'Dark Mode' : 'Light Mode' }}</span>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <div class="footer-left">
          <t-button variant="outline" theme="default" @click="handleFormat"> 自动格式化 </t-button>
        </div>

        <div class="footer-right">
          <t-button variant="outline" @click="handleClose">取消</t-button>
          <t-button theme="primary" :loading="props.loading" @click="handleConfirm"> 保存 </t-button>
        </div>
      </div>
    </template>
  </t-dialog>
</template>

<style scoped lang="less">
.editor-container {
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-medium);
  overflow: hidden;
}

.editor-status-bar {
  margin-top: 8px;
  display: flex;
  gap: 16px;
  font-size: 12px;
  color: var(--td-text-color-placeholder);
  justify-content: flex-end;

  .mode-tag {
    color: var(--td-brand-color);
  }
}

:deep(.t-dialog) {
  max-width: 95vw;
}

:deep(.cm-editor) {
  &.cm-focused {
    outline: none;
  }
}
.dialog-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.footer-right {
  display: flex;
  gap: 8px;
}
</style>
