<script setup lang="ts">
import { ref, watch } from 'vue';
import { useInstanceHubStore } from '@/store/modules/instanceHub';
import { MessagePlugin } from 'tdesign-vue-next';
import { getOnlinePlayers } from '@/api/instance';
import { UserIcon, UserClearIcon, SecuredIcon, CloseCircleIcon, UsergroupIcon } from 'tdesign-icons-vue-next';
import HurryUpppppppp from '@/components/HurryUpppppppp.vue';
import { useRoute } from 'vue-router';

const route = useRoute();
const props = defineProps<{
  visible: boolean;
  serverId: number;
  isRunning: boolean;
}>();

const emits = defineEmits<{
  'update:visible': [value: boolean];
}>();

const hubStore = useInstanceHubStore();
const activeTab = ref('online');
const onlinePlayers = ref<string[]>([]);
const loading = ref(false);

// 监听弹窗打开
watch(
  () => props.visible,
  async (val) => {
    if (route.name !== 'InstanceConsole') {
      return;
    }
    if (val && props.isRunning) {
      await fetchPlayers();
    }
  },
);

const fetchPlayers = async () => {
  if (!props.isRunning) return;
  loading.value = true;
  try {
    onlinePlayers.value = await getOnlinePlayers(props.serverId);
  } catch (error: any) {
    MessagePlugin.error(`获取列表失败: ${error.message}`);
  } finally {
    loading.value = false;
  }
};

const sendCmd = async (cmd: string, successMsg: string) => {
  if (!props.isRunning) {
    MessagePlugin.warning('实例未运行');
    return;
  }
  try {
    await hubStore.sendCommand(cmd);
    MessagePlugin.success(successMsg);
  } catch (error: any) {
    MessagePlugin.error(`执行失败: ${error.message}`);
  }
};

const handleClose = () => {
  emits('update:visible', false);
};
</script>

<template>
  <t-dialog
    attach="body"
    :visible="visible"
    header="玩家管理"
    width="min(800px, 95vw)"
    placement="center"
    :footer="false"
    @close="handleClose"
  >
    <t-tabs v-model="activeTab" theme="card">
      <t-tab-panel value="online" label="在线玩家">
        <template #label><user-icon style="margin-right: 4px" /> 在线玩家</template>
        <div class="panel-content">
          <div class="toolbar">
            <t-button theme="primary" variant="outline" :disabled="!isRunning" :loading="loading" @click="fetchPlayers"
              >刷新列表</t-button
            >
          </div>

          <t-list v-if="onlinePlayers.length > 0" :split="true" class="player-list">
            <t-list-item v-for="player in onlinePlayers" :key="player" class="custom-list-item">
              <div class="custom-item-layout">
                <div class="player-info">
                  <t-avatar shape="round" :image="`https://minotar.net/helm/${player}/32.png`" />
                  <span class="name">{{ player }}</span>
                </div>

                <div class="player-actions">
                  <t-space class="action-space" break-line>
                    <t-button
                      size="small"
                      variant="text"
                      theme="primary"
                      @click="sendCmd(`op ${player}`, `已提升 ${player} 为管理员`)"
                      >设为 OP</t-button
                    >
                    <t-button
                      size="small"
                      variant="text"
                      theme="warning"
                      @click="sendCmd(`deop ${player}`, `已撤销 ${player} 的管理员`)"
                      >撤销 OP</t-button
                    >
                    <t-button
                      size="small"
                      variant="text"
                      theme="danger"
                      @click="sendCmd(`kick ${player} 被管理员踢出`, `已踢出 ${player}`)"
                      >踢出</t-button
                    >
                    <t-button
                      size="small"
                      variant="text"
                      theme="danger"
                      @click="sendCmd(`ban ${player} 已被服务器管理员封禁`, `已封禁 ${player}`)"
                      >封禁</t-button
                    >
                  </t-space>
                </div>
              </div>
            </t-list-item>
          </t-list>
          <div v-else class="empty-state">
            <user-clear-icon size="48px" style="color: var(--td-text-color-placeholder); margin-bottom: 12px" />
            <p>{{ isRunning ? '当前没有玩家在线' : '服务器未运行' }}</p>
          </div>
        </div>
      </t-tab-panel>

      <t-tab-panel value="ops" label="管理员 (OP)">
        <template #label><secured-icon style="margin-right: 4px" /> 管理员</template>
        <div class="panel-content empty-state">
          <hurry-upppppppp />
        </div>
      </t-tab-panel>

      <t-tab-panel value="banned" label="黑名单">
        <template #label><close-circle-icon style="margin-right: 4px" /> 黑名单</template>
        <div class="panel-content empty-state">
          <hurry-upppppppp />
        </div>
      </t-tab-panel>

      <t-tab-panel value="whitelist" label="白名单">
        <template #label><usergroup-icon style="margin-right: 4px" /> 白名单</template>
        <div class="panel-content empty-state">
          <hurry-upppppppp />
        </div>
      </t-tab-panel>
    </t-tabs>
  </t-dialog>
</template>

<style scoped lang="less">
.panel-content {
  padding: 16px 0;
  height: 60vh;
  min-height: 300px;
  max-height: 650px;
  overflow-y: auto;
  overflow-x: hidden;

  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    background-color: var(--td-scrollbar-color);
    border-radius: 4px;
  }
}

.toolbar {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
  padding: 0 4px;
}

.player-list {
  border: 1px solid var(--td-component-stroke);
  border-radius: var(--td-radius-default);
  overflow: hidden;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  min-height: 200px;
  color: var(--td-text-color-placeholder);
}

/* ================= 我们自己控制的布局 (核心) ================= */

/* 让 TDesign 的内容区占满 100% 宽度 */
:deep(.custom-list-item .t-list-item__content) {
  width: 100%;
}

.custom-item-layout {
  width: 100%;
  display: flex;
  justify-content: space-between; /* 桌面端：左右分布 */
  align-items: center;
  gap: 16px;
}

.player-info {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0; /* 防止子元素撑破 Flex 容器 */
  flex: 1;

  .t-avatar {
    flex-shrink: 0;
  }

  .name {
    font-weight: 500;
    font-family: var(--td-font-family-sans-serif);
    white-space: nowrap; /* 桌面端单行显示 */
    overflow: hidden;
    text-overflow: ellipsis;
  }
}

.player-actions {
  flex-shrink: 0; /* 桌面端按钮区不缩小 */
}

/* ================= 移动端适配 ================= */
@media (max-width: 640px) {
  .panel-content {
    height: 75vh;
  }

  .custom-item-layout {
    flex-direction: column; /* 手机端：上下分布 */
    align-items: stretch; /* 宽度拉伸至 100% */
    gap: 12px;
  }

  .player-info {
    width: 100%;

    .name {
      white-space: normal; /* 允许换行 */
      word-break: break-all; /* 防止超长无空格英文字符撑破 */
      line-height: 1.4;
    }
  }

  .player-actions {
    width: 100%;

    :deep(.action-space) {
      display: flex;
      flex-wrap: wrap; /* 按钮挤不下时自动换行 */
      justify-content: flex-start;
      gap: 12px 8px !important;
    }
  }
}
</style>
