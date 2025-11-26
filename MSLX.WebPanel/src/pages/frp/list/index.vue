<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ChevronRightIcon, CloudIcon, ServerIcon } from 'tdesign-icons-vue-next';

import Result from '@/components/result/index.vue';

import type { FrpListModel } from '@/api/model/frp';
import { getFrpList } from '@/api/frp';
import { changeUrl } from '@/router';

const frpList = ref<FrpListModel[]>([]);
const loading = ref(true);
const isError = ref(false);

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
    isError.value = false; // 每次请求前重置错误状态
    frpList.value = await getFrpList();
  } catch (error) {
    console.error(error);
    isError.value = true; // 标记发生错误
  } finally {
    loading.value = false;
  }
}

const handleCardClick = (item: FrpListModel) => {
  changeUrl(`/frp/console/${item.id}`);
};


onMounted(() => {
  getList();
});
</script>

<template>
  <div class="dashboard-wrapper">
    <div class="content-container">

      <!-- 加载状态 -->
      <t-loading v-if="loading" size="medium" text="加载中..." class="loading-box" />

      <!-- 错误状态 (获取失败) -->
      <result
        v-else-if="isError"
        title="获取数据失败"
        tip="无法连接到服务器，请检查网络或稍后重试"
        type="500"
      >
        <t-button @click="getList">重试</t-button>
      </result>

      <!-- 空状态 (没有数据) -->
      <result
        v-else-if="frpList.length === 0"
        title="暂无隧道"
        tip="您还没有创建任何 Frp 隧道，快去创建一个吧"
        type="404"
      >
        <t-button @click="changeUrl('/frp/create')">创建隧道</t-button>
      </result>

      <!-- 列表展示 -->
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
