<script setup lang="ts">
// 导入 TDesign 组件
import { Card as TCard, Tag as TTag, Icon as TIcon } from 'tdesign-vue-next';
</script>

<template>
  <t-card :bordered="false" shadow style="width: 100%">
    <div class="info-grid">
      <div class="info-item">
        <span class="info-label">
          <t-icon name="server" />
          <span>实例数量：</span>
        </span>
        <span class="info-value">10</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="logo-codepen" />
          <span>NET环境：</span>
        </span>
        <span class="info-value">6.0.8</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="dashboard" />
          <span>面板版本：</span>
        </span>
        <span class="info-value">v1.2.3</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="cloud" />
          <span>节点版本：</span>
        </span>
        <span class="info-value">v1.2.3</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="desktop" />
          <span>主机名：</span>
        </span>
        <span class="info-value">MyHost-Server-01</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="system-code" />
          <span>系统类型：</span>
        </span>
        <span class="info-value">Linux (amd64)</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="system-setting" />
          <span>系统版本：</span>
        </span>
        <span class="info-value">Ubuntu 22.04 LTS</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="check-circle" />
          <span>版本状态：</span>
        </span>
        <span class="info-value">
          <t-tag theme="success" variant="light">匹配</t-tag>
        </span>
      </div>
    </div>
  </t-card>
</template>

<style scoped lang="less">
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 16px; // 网格间距
}

.info-item {
  display: flex;
  align-items: center;
  justify-content: flex-start; // 确保内容从左开始
  padding: 10px 14px;

  background-color: var(--td-bg-color-container-hover);
  border-radius: var(--td-radius-medium);
  font-size: var(--td-font-size-m);
}

.info-label {
  display: flex;
  align-items: center;
  gap: 6px; // 图标和文字的间距

  color: var(--td-text-color-secondary);
  flex-shrink: 0; // 防止标签被压缩
}

.info-value {
  color: var(--td-text-color-primary);
  font-weight: 600;

  margin-left: auto;
  padding-left: 10px;

  text-align: right;
  word-break: break-all;

  :deep(.t-tag) {
    font-weight: 600;
  }
}

// 响应式
@media screen and (max-width: 500px) {
  .info-grid {
    grid-template-columns: 1fr;
  }
}
</style>
