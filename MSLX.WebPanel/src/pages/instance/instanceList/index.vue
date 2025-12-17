<script setup lang="ts">
import { onMounted, reactive } from 'vue';
import { DeleteIcon, CheckCircleFilledIcon, CloseCircleFilledIcon, CpuIcon } from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { useInstanceListStore } from '@/store/modules/instance';
import type { InstanceListModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { postDeleteInstance } from '@/api/instance';

// 导入logo资源
import neoforgedImg from '@/assets/serverLogos/neoforged.png';
import forgeImg from '@/assets/serverLogos/150px-Anvil.png';
import customImg from '@/assets/serverLogos/150px-MinecartWithCommandBlock.png';
import defaultImg from '@/assets/serverLogos/150px-Allium.png';
import { BASE_URL_NAME, TOKEN_NAME } from '@/config/global';

const store = useInstanceListStore();

onMounted(() => {
  store.refreshInstanceList();
});

const handleCardClick = (item: InstanceListModel) => {
  // 跳转服务器控制台
  changeUrl(`/instance/console/${item.id}`);
};

const getImageUrl = (name: string, id: number) => {
  if (name.includes('http')) return name;
  switch (name) {
    case 'neoforge':
      return neoforgedImg;
    case 'forge':
      return forgeImg;
    case 'custom':
      return customImg;
    case 'server-icon':
      return new URL(
        `${localStorage.getItem(BASE_URL_NAME)}/api/instance/icon/${id}.png?x-user-token=${localStorage.getItem(TOKEN_NAME)}`,
        import.meta.url,
      ).href;
    default:
      return defaultImg;
  }
};

const formatCore = (core: string) => {
  if (core === 'none') {
    return '自定义模式';
  }
  if (core.startsWith('@')) {
    if (core.includes('neoforge')) {
      return 'NeoForge';
    } else {
      return 'Forge';
    }
  } else {
    return core.replace('.jar', '');
  }
};

const deleteState = reactive({
  visible: false,
  loading: false,
  deleteFile: false,
  item: null as InstanceListModel | null,
});

const handleDelete = (e: MouseEvent, item: InstanceListModel) => {
  e.stopPropagation();

  deleteState.item = item;
  deleteState.deleteFile = false; // 默认不勾选，防止误删文件
  deleteState.loading = false;

  deleteState.visible = true;
};

const handleConfirmDelete = async () => {
  if (!deleteState.item) return;

  deleteState.loading = true; // 显示按钮加载圈

  try {
    await postDeleteInstance(deleteState.item.id, deleteState.deleteFile);

    MessagePlugin.success('删除成功');
    deleteState.visible = false;

    await store.refreshInstanceList();
  } catch (e: any) {
    MessagePlugin.error('删除失败: ' + e.message);
  } finally {
    deleteState.loading = false;
  }
};
</script>

<template>
  <div class="server-list-container">
    <div class="page-header">
      <h2 class="title">服务端列表</h2>
      <t-space>
        <t-button theme="primary" variant="dashed" @click="store.refreshInstanceList"> 刷新列表 </t-button>
        <t-button theme="primary" @click="changeUrl('/instance/create')"> 添加服务端 </t-button></t-space
      >
    </div>

    <t-loading :loading="false" text="加载中..." fullscreen />

    <t-row :gutter="[24, 24]">
      <t-col v-for="item in store.instanceList" :key="item.id" :xs="12" :sm="6" :md="4" :lg="3" :xl="3">
        <t-card class="server-card" :class="{ 'status-running': item.status }" :bordered="false" @click="handleCardClick(item)">
          <div class="card-header">
            <div class="icon-wrapper" :class="{ 'is-running': item.status }">
              <t-avatar :image="getImageUrl(item.icon, item.id)" size="large" shape="round" class="server-icon" />
            </div>

            <div class="status-badge">
              <t-tag v-if="item.status" theme="success" variant="light" shape="round">
                <template #icon><check-circle-filled-icon /></template>
                运行中
              </t-tag>
              <t-tag v-else theme="default" variant="light" shape="round">
                <template #icon><close-circle-filled-icon /></template>
                未启动
              </t-tag>
            </div>
          </div>

          <div class="card-content">
            <h3 class="server-name text-ellipsis">{{ item.name }}</h3>

            <div class="server-info">
              <div class="info-item">
                <cpu-icon class="info-icon" />
                <span>{{ formatCore(item.core) }}</span>
              </div>
              <div class="info-item id-tag">
                <span>#{{ item.id }}</span>
              </div>
            </div>
          </div>

          <div class="card-actions">
            <span class="action-hint">点击管理</span>

            <t-button
              shape="circle"
              theme="danger"
              variant="text"
              class="delete-btn"
              @click="(e) => handleDelete(e, item)"
            >
              <delete-icon />
            </t-button>
          </div>
        </t-card>
      </t-col>
    </t-row>

    <div v-if="store.instanceList.length === 0" class="empty-state">
      <t-empty description="暂无服务端实例" />
    </div>

    <t-dialog
      v-model:visible="deleteState.visible"
      header="确认删除"
      theme="danger"
      :confirm-btn="{ content: '确认删除', loading: deleteState.loading }"
      @confirm="handleConfirmDelete"
    >
      <div class="delete-dialog-body">
        <p>
          您确定要删除服务端 <strong>{{ deleteState.item?.name }}</strong> (ID: {{ deleteState.item?.id }}) 吗？
        </p>
        <p class="warning-text">此操作不可恢复！</p>

        <div class="checkbox-area">
          <t-checkbox v-model="deleteState.deleteFile">同时删除服务端文件数据</t-checkbox>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
@card-radius: 16px;
@transition-speed: 0.3s;

.server-list-container {
  padding: 12px;

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
}

.server-card {
  position: relative;
  border-radius: @card-radius;
  background: var(--td-bg-color-container);
  transition: all @transition-speed cubic-bezier(0.34, 1.56, 0.64, 1);
  cursor: pointer;
  overflow: hidden;
  box-shadow: var(--td-shadow-1);
  border: 1px solid transparent;

  // 运行中绿色条
  &::before {
    content: "";
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 5.1px; // 反正就是这个数好看qwq
    background: transparent;
    transition: background-color 0.3s;
    z-index: 1;
    background: var(--td-component-stroke); // 没运行的颜色
    border-top-left-radius: @card-radius;
    border-top-right-radius: @card-radius;
  }

  &.status-running::before {
    background: var(--td-success-color);
  }

  // 悬浮效果：上浮 + 阴影增强 + 边框高亮
  &:hover {
    transform: translateY(-6px);
    box-shadow: var(--td-shadow-3);
    border-color: var(--td-brand-color-light);

    .delete-btn {
      opacity: 1;
      transform: scale(1);
    }

    .action-hint {
      opacity: 1;
      transform: translateX(0);
    }
  }

  // 内部布局
  :deep(.t-card__body) {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
}

// 头部区域样式
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;

  .icon-wrapper {
    position: relative;
    padding: 2px;
    border-radius: 14px;
    border: 2px solid transparent;
    transition: border-color 0.3s;

    // 如果是运行状态，给头像加个绿色光环
    &.is-running {
      border-color: var(--td-success-color);
    }

    .server-icon {
      background-color: var(--td-bg-color-secondarycontainer);
      color: var(--td-brand-color);
    }
  }
}

// 内容区域样式
.card-content {
  .server-name {
    font-size: 18px;
    font-weight: 700;
    color: var(--td-text-color-primary);
    margin: 0 0 8px 0;
    line-height: 1.4;
  }

  .server-info {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-size: 13px;
    color: var(--td-text-color-secondary);

    .info-item {
      display: flex;
      align-items: center;
      gap: 4px;
      background: var(--td-bg-color-secondarycontainer);
      padding: 4px 10px;
      border-radius: 8px;

      .info-icon {
        font-size: 14px;
      }
    }

    .id-tag {
      background: transparent;
      border: 1px solid var(--td-component-border);
      color: var(--td-text-color-placeholder);
      font-family: monospace;
    }
  }
}

// 底部操作栏样式
.card-actions {
  margin-top: 8px;
  padding-top: 16px;
  border-top: 1px dashed var(--td-component-stroke);
  display: flex;
  justify-content: space-between;
  align-items: center;
  height: 32px;

  .action-hint {
    font-size: 12px;
    color: var(--td-brand-color);
    font-weight: 500;
    opacity: 0; // 默认隐藏，hover显示
    transform: translateX(-10px);
    transition: all 0.3s ease;
  }

  .delete-btn {
    opacity: 0; // 默认隐藏，保持界面清爽
    transform: scale(0.8);
    transition: all 0.2s ease;
    margin-left: auto; // 靠右

    &:hover {
      background: var(--td-error-color-light);
      color: var(--td-error-color);
    }
  }

  // 移动端默认显示删除按钮
  @media (max-width: 768px) {
    .delete-btn {
      opacity: 1;
      transform: scale(1);
    }
  }
}

// 通用工具类
.text-ellipsis {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.empty-state {
  padding: 60px 0;
}

// 弹窗
.warning-text {
  color: var(--td-error-color);
  font-size: 12px;
  margin-bottom: 10px;
}
.checkbox-area {
  margin-top: 15px;
  padding-top: 10px;
  border-top: 1px dashed var(--td-component-stroke);
}
</style>
