<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import {
  Card as TCard,
  Loading as TLoading,
  Icon as TIcon,
} from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from "@vueuse/core";

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
  const fallbackMarkdown = "## 🔴 公告加载失败\n- 请检查网络连接或联系管理员。";

  try {
    const res = await request.get({
      url: 'https://api.mslmc.cn/v3/query/notice?query=mslxNoticeMd'
    });

    if (res && res.mslxNoticeMd) {
      notice.value = res.mslxNoticeMd;
    } else {
      notice.value = fallbackMarkdown;
    }
  } catch (err) {
    console.error("获取公告失败:", err);
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
  <t-card shadow :bordered="false" class="announcement-card">
    <template #title>
      <div class="card-header">
        <t-icon name="system-messages" />
        <span>系统公告</span>
      </div>
    </template>

    <t-loading :loading="loading" text="加载中..." size="small" style="width: 100%">
      <div class="announcement-wrapper">
        <md-preview
          editor-id="announcement-preview"
          :model-value="notice"
          :theme="mdTheme as Themes"
          class="md-preview-wrapper"
        />
      </div>
    </t-loading>
  </t-card>
</template>

<style scoped lang="less">
.announcement-card {
  width: 100%;
  transition: all 0.3s;
  border-radius: 6px;
  background-color: var(--td-bg-color-container);

  // 头部样式微调
  :deep(.t-card__header) {
    padding: var(--td-comp-paddingTB-l) var(--td-comp-paddingLR-l);
  }

  :deep(.t-card__body) {
    padding: var(--td-comp-paddingTB-m) var(--td-comp-paddingLR-l);
  }
}

.card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 16px; // 保持与其他标题一致的大小
  font-weight: 600;
  color: var(--td-text-color-primary);
}

// --- 内容包裹器 ---
.announcement-wrapper {
  min-height: 150px;
  overflow-y: auto;
  width: 100%;

  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    background-color: var(--td-scrollbar-color);
    border-radius: 3px;
  }
}

// md内容

.md-preview-wrapper {
  background: none; // 确保背景透明，使用卡片的背景色
}

// 覆盖MD编辑器链接颜色
:deep(.md-editor-preview a){
  color: var(--td-brand-color);
  text-decoration: none;
  &:hover {
    text-decoration: underline;
  }
}

// 覆盖代码块颜色
:deep(.md-editor-preview code){
  color: var(--td-brand-color);
  background-color: color-mix(in srgb, var(--td-brand-color), transparent 90%);
  border-radius: 4px;
  padding: 2px 4px;
}

// 引用块左边框颜色
:deep(.md-editor div.default-theme){
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
