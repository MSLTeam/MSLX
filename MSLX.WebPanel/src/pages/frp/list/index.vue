<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { ChevronRightIcon, CloudIcon, ServerIcon, DeleteIcon } from 'tdesign-icons-vue-next';

import Result from '@/components/result/index.vue';

import type { FrpListModel } from '@/api/model/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { getFrpAutoStartList, postChangeFrpAutoStartList, postDeleteFrpTunnel } from '@/api/frp';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';

const tunnelsStore = useTunnelsStore();

const loading = ref(true);
const isError = ref(false);

// 自启动列表数据
const autoStartState = reactive({
  visible: false,
  loading: false,
  submitting: false,
  selectedIds: [] as number[],
});

// 打开设置弹窗
const openAutoStartSettings = async () => {
  autoStartState.visible = true;
  autoStartState.loading = true;
  try {
    if (tunnelsStore.frpList.length === 0) {
      await tunnelsStore.getTunnels();
    }
    const res = await getFrpAutoStartList();
    autoStartState.selectedIds = res || [];
  } catch (error) {
    MessagePlugin.error('获取自启动配置失败 ' + error.message);
  } finally {
    autoStartState.loading = false;
  }
};

// 保存设置
const handleSaveAutoStart = async () => {
  autoStartState.submitting = true;
  try {
    await postChangeFrpAutoStartList(autoStartState.selectedIds);
    MessagePlugin.success('自启动设置已更新');
    autoStartState.visible = false;
  } catch (error: any) {
    MessagePlugin.error('保存失败: ' + error.message);
  } finally {
    autoStartState.submitting = false;
  }
};

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
    await tunnelsStore.getTunnels();
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

const handleDelete = (id: number) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除隧道?',
    body: '删除后该隧道将无法恢复。确定要继续吗？',
    theme: 'danger',
    onConfirm: async () => {
      try {
        await postDeleteFrpTunnel(id);
        MessagePlugin.success(`隧道 ${id} 删除成功`);
        await getList();
        confirmDialog.hide();
      } catch (error) {
        MessagePlugin.error(error.message);
      }
    },
    onClose: () => {
      confirmDialog.hide();
    },
  });
};

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="dashboard-wrapper">
    <div class="content-container">
      <div class="page-header">
        <h2 class="title">隧道列表</h2>
        <t-space>
          <t-button theme="primary" variant="dashed" @click="getList"> 刷新列表 </t-button>
          <t-button theme="default" variant="outline" @click="openAutoStartSettings"> 设置自启动 </t-button>
          <t-button theme="primary" @click="changeUrl('/frp/create')"> 创建隧道 </t-button>
        </t-space>
      </div>

      <!-- 加载状态 -->
      <t-loading v-if="loading" size="medium" text="加载中..." class="loading-box" />

      <!-- 错误状态 (获取失败) -->
      <result v-else-if="isError" title="获取数据失败" tip="无法连接到服务器，请检查网络或稍后重试" type="500">
        <t-button @click="getList">重试</t-button>
      </result>

      <!-- 空状态 (没有数据) -->
      <result
        v-else-if="tunnelsStore.frpList.length === 0"
        title="暂无隧道"
        tip="您还没有创建任何 Frp 隧道，快去创建一个吧"
        type="404"
      >
        <t-button @click="changeUrl('/frp/create')">创建隧道</t-button>
      </result>

      <!-- 列表展示 -->
      <t-row v-else :gutter="[20, 20]">
        <t-col v-for="item in tunnelsStore.frpList" :key="item.id" :span="24" :xs="12" :sm="6" :md="6" :lg="4" :xl="4">
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
                <t-tag variant="light" :theme="item.status ? 'success' : 'warning'">{{
                  item.status ? '运行中' : '未运行'
                }}</t-tag>
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

                <t-button
                  shape="circle"
                  theme="danger"
                  size="small"
                  @click="handleDelete(item.id)"
                  @click.stop="() => {}"
                >
                  <template #icon> <delete-icon /></template>
                </t-button>
              </div>

              <div class="hover-action">
                <chevron-right-icon />
              </div>
            </div>
          </div>
        </t-col>
      </t-row>
    </div>
    <t-dialog
      v-model:visible="autoStartState.visible"
      header="设置开机自启动隧道"
      width="600px"
      :confirm-btn="{ content: '保存设置', loading: autoStartState.submitting }"
      @confirm="handleSaveAutoStart"
    >
      <t-loading :loading="autoStartState.loading" text="正在读取配置..." size="small">
        <div class="auto-start-content">
          <t-alert theme="info" message="勾选的隧道将在守护进程启动时自动运行" style="margin-bottom: 16px" />

          <t-checkbox-group v-model="autoStartState.selectedIds" class="tunnel-checkbox-group">
            <t-row :gutter="[16, 16]">
              <t-col v-for="item in tunnelsStore.frpList" :key="item.id" :span="12">
                <t-checkbox :value="item.id" class="tunnel-item">
                  <div class="tunnel-label">
                    <span class="tag"
                      ><t-tag theme="primary">{{ item.service }}</t-tag></span
                    >
                    <span class="name">{{ item.name }}</span>
                    <span class="id">#{{ item.id }}</span>
                  </div>
                </t-checkbox>
              </t-col>
            </t-row>
          </t-checkbox-group>

          <div v-if="tunnelsStore.frpList.length === 0" class="empty-tip">暂无可用隧道</div>
        </div>
      </t-loading>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
.content-container {
  padding: 12px;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;

  .title {
    font-size: 24px;
    font-weight: 700;
    color: var(--td-text-color-primary);
    margin: 0;
  }
}

// 设置自启动弹窗内样式
.auto-start-content {
  max-height: 400px;
  overflow-y: auto;
  padding: 4px;
}

.tunnel-checkbox-group {
  width: 100%;
}

.tunnel-item {
  width: 100%;
  padding: 8px;
  border-radius: 6px;
  transition: background 0.2s;

  &:hover {
    background: var(--td-bg-color-secondarycontainer);
  }

  :deep(.t-checkbox__label) {
    width: 100%;
  }

  .tunnel-label {
    display: flex;
    justify-content: space-between;
    width: 100%;

    .name {
      font-weight: 500;
      color: var(--td-text-color-primary);
      margin-left: 5px;
    }

    .name {
      font-weight: 500;
      color: var(--td-text-color-primary);
    }

    .id {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-left: 5px;
    }
  }
}

.empty-tip {
  text-align: center;
  color: var(--td-text-color-placeholder);
  padding: 20px 0;
}

.dashboard-wrapper {
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
    align-items: center;
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
