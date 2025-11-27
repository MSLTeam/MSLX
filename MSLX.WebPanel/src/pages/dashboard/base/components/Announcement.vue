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
      url: 'https://api.mslmc.cn/v3/query/notice?query=noticeMd'
    });

    if (res && res.noticeMd) {
      notice.value = res.noticeMd;
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
  <t-card :bordered="false" shadow style="width: 100%">
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
:deep(.md-editor-preview a){
  color: var(--td-brand-color) !important;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: var(--td-font-size-large);
  font-weight: 600;
}

// “更多”按钮样式
.card-action {
  color: var(--td-brand-color);
  font-size: var(--td-font-size-m);
  text-decoration: none;
  &:hover {
    color: var(--td-brand-color-hover);
  }
}

// 内容包裹器
.announcement-wrapper {
  min-height: 150px;
  overflow-y: auto;
  width: 100%;
}

// MdPreview 样式适配
.md-preview-wrapper {
  background: none;
}

:deep(.md-editor-dark) {
  --md-color: rgba(255, 255, 255, 90%);

  --md-bk-color: var(--td-bg-color-container);
}

// 适配亮色模式
:deep(.md-editor-light) {
  --md-bk-color: var(--td-bg-color-container);
}
</style>
