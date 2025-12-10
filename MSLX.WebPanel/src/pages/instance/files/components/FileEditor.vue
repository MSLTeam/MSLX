<script setup lang="ts">
import { ref, watch, computed, onMounted, onUnmounted } from 'vue';
import { Codemirror } from 'vue-codemirror';
import { oneDark } from '@codemirror/theme-one-dark';
import { json } from '@codemirror/lang-json';
import { yaml } from '@codemirror/lang-yaml';
import { javascript } from '@codemirror/lang-javascript';
import { css } from '@codemirror/lang-css';
import { html } from '@codemirror/lang-html';

const props = defineProps({
  visible: Boolean,
  fileName: String,
  content: String,
  loading: Boolean
});

const emit = defineEmits(['update:visible', 'save']);

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
    attributeFilter: ['theme-mode']
  });
});

onUnmounted(() => {
  observer?.disconnect();
});

// --- 初始化内容 ---
watch(() => props.content, (newVal) => {
  code.value = newVal || '';
}, { immediate: true });

// --- 动态计算编辑器扩展 ---
const extensions = computed(() => {
  const result = [];

  if (isDarkMode.value) {
    result.push(oneDark);
  }

  // 匹配语言
  const ext = props.fileName?.split('.').pop()?.toLowerCase();
  switch (ext) {
    case 'json': result.push(json()); break;
    case 'yml':
    case 'yaml': result.push(yaml()); break;
    case 'js':
    case 'ts': result.push(javascript()); break;
    case 'css':
    case 'less':
    case 'scss': result.push(css()); break;
    case 'html':
    case 'xml': result.push(html()); break;
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
    :confirm-btn="{ content: '保存', loading: props.loading, theme: 'primary' }"
    class="editor-dialog"
    @close="handleClose"
    @confirm="handleConfirm"
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
</style>
