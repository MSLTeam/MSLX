<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import { Loading as TLoading, Icon as TIcon } from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from '@vueuse/core';

const loading = ref(true);
const notice = ref('');

// 暗黑模式逻辑
const isDark = useDark();
const mdTheme = ref(isDark.value ? 'dark' : 'light');
watch(isDark, () => {
  mdTheme.value = isDark.value ? 'dark' : 'light';
});

async function fetchAnnouncement() {
  loading.value = true;
  const fallbackMarkdown = '## 🔴 公告加载失败\n- 请检查网络连接或联系管理员。';

  try {
    const res = await request.get({
      url: 'https://api.mslmc.cn/v3/query/notice?query=mslxNoticeMd',
    });

    if (res && res.mslxNoticeMd) {
      notice.value = res.mslxNoticeMd;
    } else {
      notice.value = fallbackMarkdown;
    }
  } catch (err) {
    console.error('获取公告失败:', err);
    notice.value = fallbackMarkdown;
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  fetchAnnouncement();
});
</script>

<template>
  <div class="design-card w-full bg-white dark:bg-zinc-800 rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300 flex flex-col relative overflow-hidden">

    <div class="flex items-center gap-2 p-5 sm:px-6 pb-4  dark:border-zinc-700/50 text-left">
      <t-icon name="system-messages" class="text-[var(--color-primary)] text-lg" />
      <h3 class="text-[16px] font-bold text-zinc-900 dark:text-zinc-100 m-0">系统公告</h3>
    </div>

    <div class="p-5 sm:px-6 text-left w-full min-h-[150px]">
      <t-loading :loading="loading" text="加载中..." size="small" class="w-full">
        <div class="w-full overflow-y-auto custom-scrollbar">
          <md-preview
            editor-id="announcement-preview"
            :model-value="notice"
            :theme="mdTheme as Themes"
            class="custom-md-preview bg-transparent text-left !p-0"
          />
        </div>
      </t-loading>
    </div>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

// 自定义滚动条样式
.custom-scrollbar {
  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
  @apply bg-zinc-300 dark:bg-zinc-600 rounded-full;
  }
}

// ================= Markdown 组件样式 =================

// 强制背景透明与左对齐
:deep(.custom-md-preview) {
  --md-bk-color: transparent !important;
  --md-color: inherit !important;
  text-align: left !important;
}

// 覆盖MD编辑器链接颜色，使用主题色
:deep(.md-editor-preview a) {
  color: var(--color-primary);
  text-decoration: none;
  &:hover {
    text-decoration: underline;
  }
}

// 覆盖行内代码块颜色，采用微透明质感
:deep(.md-editor-preview code:not([class*="language-"])) {
  color: var(--color-primary);
  background-color: color-mix(in srgb, var(--color-primary), transparent 90%);
  border-radius: 4px;
  padding: 2px 4px;
}

:deep(.md-editor-preview blockquote){
  background: none;
}

// 引用块左边框颜色
:deep(.md-editor div.default-theme) {
  --md-theme-quote-border: 4px solid var(--color-primary);
}

// 颜色模式适配，跟随外层文字颜色
:deep(.md-editor-preview) {
  --md-color: inherit !important;
}
</style>
