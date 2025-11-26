<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ChevronRightIcon, CloudIcon, ServerIcon } from 'tdesign-icons-vue-next';

import type { FrpListModel } from '@/api/model/frp';
import { getFrpList } from '@/api/frp';

const frpList = ref<FrpListModel[]>([]);
const loading = ref(true);

// 配置文件 → 颜色
const getConfigTheme = (type: string) => {
  const map: Record<string, string> = {
    toml: 'primary',
    ini: 'warning',
    cmd: 'danger',
    json: 'success',
  };
  return map[type] || 'default';
};

async function getList() {
  try {
    loading.value = true;
    frpList.value = await getFrpList();
  } catch (error) {
    console.error(error);
  } finally {
    loading.value = false;
  }
}

const handleCardClick = (item: FrpListModel) => {
  console.log('跳转详情:', item);
};

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="dashboard-wrapper">
    <div class="content-container">
      <t-loading v-if="loading" size="medium" text="加载中..." class="loading-box" />

      <t-row v-else :gutter="[20, 20]">
        <t-col v-for="item in frpList" :key="item.id" :xs="24" :sm="12" :md="6" :lg="4" :xl="4">
          <div class="design-card" @click="handleCardClick(item)">
            <div
              class="status-bar"
              :style="{ background: item.status ? 'var(--td-success-color)' : 'var(--td-component-stroke)' }"
            ></div>

            <div class="card-inner">
              <div class="header-row">
                <div class="icon-box">
                  <server-icon />
                </div>
                <t-tag variant="light" :theme="item.status ? 'success' : 'warning'">{{ item.status ? '运行中' : '未运行' }}</t-tag>
              </div>

              <div class="title-section">
                <h3 class="card-title">{{ item.name }}</h3>
                <div class="card-subtitle">ID: {{ item.id }}</div>
              </div>

              <div class="tags-row">
                <t-tag size="small" variant="light" shape="round">
                  <template #icon><cloud-icon /></template>
                  {{ item.service }}
                </t-tag>
                <t-tag size="small" :theme="getConfigTheme(item.configType) as any" variant="outline" shape="round">
                  {{ item.configType }}
                </t-tag>
              </div>

              <div class="hover-action">
                <chevron-right-icon />
              </div>
            </div>
          </div>
        </t-col>
      </t-row>
    </div>
  </div>
</template>

<style scoped lang="less">
.dashboard-wrapper {
  background-color: var(--td-bg-color-page);
  min-height: 100vh;
  width: 100%;
  box-sizing: border-box;
}

.loading-box {
  display: flex;
  justify-content: center;
  padding-top: 100px;
}

.design-card {
  position: relative;
  background-color: var(--td-bg-color-container);
  border: 1px solid var(--td-border-level-2-color);
  border-radius: 8px;
  overflow: hidden;
  cursor: pointer;
  transition: all 0.25s ease-in-out;
  height: 100%;
  display: flex;
  flex-direction: column;

  &:hover {
    border-color: var(--td-brand-color);
    transform: translateY(-4px);
    box-shadow: var(--td-shadow-2);

    .hover-action {
      opacity: 1;
      transform: translateX(0);
    }
  }

  .status-bar {
    height: 3px;
    width: 100%;
    transition: background 0.3s;
  }

  .card-inner {
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    position: relative;
  }

  .header-row {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;

    .icon-box {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      background: var(--td-bg-color-secondarycontainer);
      color: var(--td-brand-color);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 18px;
    }

    .status-badge {
      font-size: 12px;
      padding: 2px 8px;
      border-radius: 4px;
      background: var(--td-bg-color-component);
      color: var(--td-text-color-placeholder);

      &.active {
        background: var(--td-success-color-1);
        color: var(--td-success-color);
      }
    }
  }

  .title-section {
    .card-title {
      font-size: 15px;
      font-weight: 600;
      color: var(--td-text-color-primary);
      margin: 0 0 4px 0;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .card-subtitle {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      font-family: var(--td-font-family-number);
    }
  }

  .tags-row {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    margin-top: auto;
  }

  .hover-action {
    position: absolute;
    bottom: 16px;
    right: 16px;
    color: var(--td-brand-color);
    opacity: 0;
    transform: translateX(-5px);
    transition: all 0.2s;
    font-size: 16px;
  }
}
</style>
