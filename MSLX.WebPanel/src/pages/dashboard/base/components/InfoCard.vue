<script setup lang="ts">
import { Card as TCard, Tag as TTag, Icon as TIcon, Tooltip as TTooltip } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import pkg from '@/../package.json';
import { useInstanceListStore } from '@/store/modules/instance';
import { onMounted } from 'vue';
const userStore = useUserStore();
const instanceListStore = useInstanceListStore();

onMounted(() => {
  instanceListStore.refreshInstanceList();
})
</script>

<template>
  <t-card shadow :bordered="false" class="system-info-card">
    <div class="info-grid">
      <div class="info-item">
        <span class="info-label">
          <t-icon name="server" />
          <span>在线实例：</span>
        </span>
        <span class="info-value">{{ instanceListStore.onlineInstanceCount }} / {{ instanceListStore.totalInstanceCount }}</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="logo-codepen" />
          <span>NET环境：</span>
        </span>
        <span class="info-value">{{ userStore.userInfo.systemInfo.netVersion }}</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="dashboard" />
          <span>面板版本：</span>
        </span>
        <t-tooltip :content="pkg.version" :max-width="'400px'">
          <span class="info-value truncate-value">
            {{ pkg.version }}
          </span>
        </t-tooltip>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="cloud" />
          <span>节点版本：</span>
        </span>
        <t-tooltip :content="`v${userStore.userInfo.version}`" :max-width="'400px'">
          <span class="info-value truncate-value">
            v{{ userStore.userInfo.version }}
          </span>
        </t-tooltip>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="desktop" />
          <span>主机名：</span>
        </span>
        <span class="info-value truncate-value">{{ userStore.userInfo.systemInfo.hostname }}</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="system-code" />
          <span>系统类型：</span>
        </span>
        <span class="info-value">{{ userStore.userInfo.systemInfo.osType }} ({{ userStore.userInfo.systemInfo.osArchitecture }})</span>
      </div>

      <div class="info-item">
        <span class="info-label">
          <t-icon name="system-setting" />
          <span>系统版本：</span>
        </span>

        <t-tooltip :content="userStore.userInfo.systemInfo.osVersion" :max-width="'400px'">
          <span class="info-value truncate-value">
            {{ userStore.userInfo.systemInfo.osVersion }}
          </span>
        </t-tooltip>
      </div>
      <div class="info-item">
        <span class="info-label">
          <t-icon name="check-circle" />
          <span>版本匹配：</span>
        </span>
        <span class="info-value">
          <t-tag :theme="(userStore.userInfo.targetFrontendVersion.panel === pkg.version)? 'success' : 'danger'" variant="light">{{ (userStore.userInfo.targetFrontendVersion.panel === pkg.version)? '正确匹配' : '请更新' }}</t-tag>
        </span>
      </div>
    </div>
  </t-card>
</template>

<style scoped lang="less">
.system-info-card {
  width: 100%;
  transition: all 0.3s;
  border-radius: 6px;
  background-color: var(--td-bg-color-container);


  // 调整内边距
  :deep(.t-card__body) {
    padding: var(--td-comp-paddingTB-l) var(--td-comp-paddingLR-l);
  }
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 16px; // 网格间距
}

.info-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;

  background-color: color-mix(in srgb, var(--td-bg-color-container-hover), transparent 60%) !important;
  border-radius: var(--td-radius-medium);
  font-size: var(--td-font-size-m);

  // 增加一点边框或过渡让它更精致
  border: 1px solid transparent;
  transition: border-color 0.2s;
  &:hover {
    border-color: var(--td-component-stroke);
  }

  overflow: hidden;
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

  padding-left: 10px;

  text-align: right;
  word-break: break-all;

  :deep(.t-tag) {
    font-weight: 600;
  }
}

.truncate-value {
  white-space: nowrap;
  overflow: hidden; /* 隐藏溢出 */
  text-overflow: ellipsis; /* 显示省略号 */
  word-break: normal;
  min-width: 0;
}

// 响应式
@media screen and (max-width: 500px) {
  .info-grid {
    grid-template-columns: 1fr;
  }
}
</style>
