<script setup lang="ts">
import { ref, watch } from 'vue';
import { useInstanceHubStore } from '@/store/modules/instanceHub';
import { MessagePlugin } from 'tdesign-vue-next';
import {
  getOnlinePlayers,
  getHistoryPlayers,
  getWhitelist,
  addWhitelist,
  removeWhitelist,
  getOps,
  addOp,
  removeOp,
  getBannedPlayers,
  addBannedPlayer,
  removeBannedPlayer,
  getBannedIps,
  addBannedIp,
  removeBannedIp,
} from '@/api/instance';
import {
  UserIcon,
  UserClearIcon,
  SecuredIcon,
  CloseCircleIcon,
  UsergroupIcon,
  AddIcon,
  DeleteIcon,
  TimeIcon,
  RefreshIcon,
} from 'tdesign-icons-vue-next';
import { useRoute } from 'vue-router';
import type { BannedIpItem, BannedPlayerItem, OpItem, UserCacheItem, WhitelistItem } from '@/api/model/instance';

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
const banType = ref('player');
const loading = ref(false);

// 操作模式（指令/修改配置）
const opMode = ref('command');

// 数据源
const onlinePlayers = ref<string[]>([]);
const historyPlayers = ref<UserCacheItem[]>([]);
const whitelist = ref<WhitelistItem[]>([]);
const ops = ref<OpItem[]>([]);
const bannedPlayers = ref<BannedPlayerItem[]>([]);
const bannedIps = ref<BannedIpItem[]>([]);

// 输入框
const inputNewWhitelist = ref('');
const inputNewOp = ref('');
const inputNewBanPlayer = ref('');
const inputNewBanReason = ref('');
const inputNewBanIp = ref('');

// 监听运行状态，动态切换默认模式
watch(
  () => props.isRunning,
  (running) => {
    if (!running) {
      opMode.value = 'api'; // 没开服只能用API改文件
    } else {
      opMode.value = 'command'; // 开服了默认切回指令
    }
  },
  { immediate: true },
);

// 监听弹窗或Tab变化
watch([() => props.visible, activeTab, banType], async ([visible]) => {
  if (route.name !== 'InstanceConsole' || !visible) return;
  fetchCurrentTabData();
});

const fetchCurrentTabData = async () => {
  loading.value = true;
  try {
    if (activeTab.value === 'online' && props.isRunning) {
      onlinePlayers.value = await getOnlinePlayers(props.serverId);
    } else if (activeTab.value === 'history') {
      historyPlayers.value = await getHistoryPlayers(props.serverId);
    } else if (activeTab.value === 'whitelist') {
      whitelist.value = await getWhitelist(props.serverId);
    } else if (activeTab.value === 'ops') {
      ops.value = await getOps(props.serverId);
    } else if (activeTab.value === 'banned') {
      if (banType.value === 'player') bannedPlayers.value = await getBannedPlayers(props.serverId);
      else bannedIps.value = await getBannedIps(props.serverId);
    }
  } catch (error: any) {
    MessagePlugin.error(`获取数据失败: ${error.message}`);
  } finally {
    loading.value = false;
  }
};

// 命令处理
const handleAction = async (apiCall: () => Promise<any>, cmdString: string, successMsg: string) => {
  try {
    if (opMode.value === 'command' && props.isRunning) {
      await hubStore.sendCommand(cmdString);
      MessagePlugin.success(`已发送指令`);
      setTimeout(() => fetchCurrentTabData(), 1000);
    } else {
      await apiCall();
      MessagePlugin.success(successMsg);
      fetchCurrentTabData();
    }
  } catch (error: any) {
    MessagePlugin.error(`操作失败: ${error.message}`);
  }
};

// 通用纯指令
const sendCmdOnly = async (cmd: string, successMsg: string) => {
  if (!props.isRunning) return MessagePlugin.warning('实例未运行');
  try {
    await hubStore.sendCommand(cmd);
    MessagePlugin.success(successMsg);
    setTimeout(() => fetchCurrentTabData(), 1500);
  } catch (error: any) {
    MessagePlugin.error(`执行失败: ${error.message}`);
  }
};

// ================= 管理员 (OP) =================
const handleAddOp = async (name: string = inputNewOp.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  await handleAction(() => addOp(props.serverId, name), `op ${name}`, '添加管理员成功');
  if (name === inputNewOp.value) inputNewOp.value = '';
};
const handleRemoveOp = async (name: string) => {
  await handleAction(() => removeOp(props.serverId, name), `deop ${name}`, '移除管理员成功');
};

// ================= 白名单 =================
const handleAddWhitelist = async (name: string = inputNewWhitelist.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  await handleAction(() => addWhitelist(props.serverId, name), `whitelist add ${name}`, '添加白名单成功');
  if (name === inputNewWhitelist.value) inputNewWhitelist.value = '';
};
const handleRemoveWhitelist = async (name: string) => {
  await handleAction(() => removeWhitelist(props.serverId, name), `whitelist remove ${name}`, '移除白名单成功');
};

// ================= 玩家封禁 =================
const handleAddBanPlayer = async (name: string = inputNewBanPlayer.value) => {
  if (!name) return MessagePlugin.warning('请输入玩家ID');
  const reason = inputNewBanReason.value ? ` ${inputNewBanReason.value}` : '';
  await handleAction(
    () => addBannedPlayer(props.serverId, name, inputNewBanReason.value),
    `ban ${name}${reason}`,
    '封禁玩家成功',
  );
  if (name === inputNewBanPlayer.value) {
    inputNewBanPlayer.value = '';
    inputNewBanReason.value = '';
  }
};
const handleRemoveBanPlayer = async (name: string) => {
  await handleAction(() => removeBannedPlayer(props.serverId, name), `pardon ${name}`, '解封玩家成功');
};

// ================= IP 封禁 =================
const handleAddBanIp = async () => {
  if (!inputNewBanIp.value) return MessagePlugin.warning('请输入IP地址');
  const reason = inputNewBanReason.value ? ` ${inputNewBanReason.value}` : '';
  await handleAction(
    () => addBannedIp(props.serverId, inputNewBanIp.value, inputNewBanReason.value),
    `ban-ip ${inputNewBanIp.value}${reason}`,
    '封禁IP成功',
  );
  inputNewBanIp.value = '';
  inputNewBanReason.value = '';
};
const handleRemoveBanIp = async (ip: string) => {
  await handleAction(() => removeBannedIp(props.serverId, ip), `pardon-ip ${ip}`, '解封IP成功');
};

const handleClose = () => emits('update:visible', false);
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
    <div class="dialog-body-container">
      <div class="global-toolbar">
        <t-tooltip content="指令模式直接与服务端交互，API模式直接修改配置文件" placement="bottom">
          <t-radio-group v-model="opMode" variant="default-filled" size="small" :disabled="!isRunning">
            <t-radio-button value="api">API 模式</t-radio-button>
            <t-radio-button value="command">指令优先</t-radio-button>
          </t-radio-group>
        </t-tooltip>

        <t-button variant="text" theme="primary" size="small" :loading="loading" @click="fetchCurrentTabData">
          <template #icon><refresh-icon /></template> 刷新
        </t-button>
      </div>

      <t-tabs v-model="activeTab" theme="card" class="manager-tabs">
        <t-tab-panel value="online" label="在线玩家">
          <template #label><user-icon style="margin-right: 4px" /> 在线玩家</template>
          <div class="panel-content">
            <t-list v-if="onlinePlayers.length > 0" :split="true" class="custom-list">
              <t-list-item v-for="player in onlinePlayers" :key="player" class="custom-list-item">
                <div class="custom-item-layout">
                  <div class="player-info">
                    <t-avatar shape="round" :image="`https://minotar.net/helm/${player}/32.png`" />
                    <span class="name">{{ player }}</span>
                  </div>
                  <div class="player-actions">
                    <t-space class="action-space" break-line>
                      <t-button size="small" variant="text" theme="primary" @click="handleAddOp(player)"
                        >设为 OP</t-button
                      >
                      <t-button size="small" variant="text" theme="warning" @click="handleRemoveOp(player)"
                        >撤销 OP</t-button
                      >
                      <t-button size="small" variant="text" theme="success" @click="handleAddWhitelist(player)"
                        >加白</t-button
                      >
                      <t-button
                        size="small"
                        variant="text"
                        theme="danger"
                        @click="sendCmdOnly(`kick ${player} 被管理员踢出`, `已踢出 ${player}`)"
                        >踢出</t-button
                      >
                      <t-button size="small" variant="text" theme="danger" @click="handleAddBanPlayer(player)"
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

        <t-tab-panel value="history" label="历史玩家">
          <template #label><time-icon style="margin-right: 4px" /> 历史玩家</template>
          <div class="panel-content">
            <t-list v-if="historyPlayers.length > 0" :split="true" class="custom-list">
              <t-list-item v-for="user in historyPlayers" :key="user.uuid" class="custom-list-item">
                <div class="custom-item-layout">
                  <div class="player-info">
                    <t-avatar shape="round" :image="`https://minotar.net/helm/${user.name}/32.png`" />
                    <div class="info-text">
                      <span class="name">{{ user.name }}</span>
                      <span class="sub-text">UUID: {{ user.uuid.split('-')[0] }}...</span>
                    </div>
                  </div>
                  <div class="player-actions">
                    <t-space class="action-space" break-line>
                      <t-button size="small" variant="text" theme="primary" @click="handleAddOp(user.name)"
                        >设为 OP</t-button
                      >
                      <t-button size="small" variant="text" theme="success" @click="handleAddWhitelist(user.name)"
                        >加白名单</t-button
                      >
                      <t-button size="small" variant="text" theme="danger" @click="handleAddBanPlayer(user.name)"
                        >封禁</t-button
                      >
                    </t-space>
                  </div>
                </div>
              </t-list-item>
            </t-list>
            <div v-else class="empty-state">无历史登录记录</div>
          </div>
        </t-tab-panel>

        <t-tab-panel value="ops" label="管理员">
          <template #label><secured-icon style="margin-right: 4px" /> 管理员</template>
          <div class="panel-content">
            <div class="input-toolbar">
              <t-input v-model="inputNewOp" placeholder="输入玩家游戏ID" @enter="handleAddOp()" clearable />
              <t-button theme="primary" @click="handleAddOp()"
                ><template #icon><add-icon /></template> 添加</t-button
              >
            </div>
            <t-list v-if="ops.length > 0" :split="true" class="custom-list">
              <t-list-item v-for="op in ops" :key="op.uuid" class="custom-list-item">
                <div class="custom-item-layout">
                  <div class="player-info">
                    <t-avatar shape="round" :image="`https://minotar.net/helm/${op.name}/32.png`" />
                    <span class="name"
                      >{{ op.name }}
                      <t-tag theme="success" variant="light" size="small">等级: {{ op.level }}</t-tag></span
                    >
                  </div>
                  <div class="player-actions">
                    <t-popconfirm content="确定要撤销该管理员吗？" theme="danger" @confirm="handleRemoveOp(op.name)">
                      <t-button size="small" variant="text" theme="danger"
                        ><template #icon><delete-icon /></template> 移除</t-button
                      >
                    </t-popconfirm>
                  </div>
                </div>
              </t-list-item>
            </t-list>
            <div v-else class="empty-state">暂无管理员记录</div>
          </div>
        </t-tab-panel>

        <t-tab-panel value="banned" label="黑名单">
          <template #label><close-circle-icon style="margin-right: 4px" /> 黑名单</template>
          <div class="panel-content">
            <div class="input-toolbar">
              <t-radio-group v-model="banType" variant="default-filled">
                <t-radio-button value="player">玩家封禁</t-radio-button>
                <t-radio-button value="ip">IP 封禁</t-radio-button>
              </t-radio-group>
            </div>

            <div v-if="banType === 'player'">
              <div class="input-toolbar">
                <t-input v-model="inputNewBanPlayer" placeholder="输入玩家ID" style="flex: 1" clearable />
                <t-input v-model="inputNewBanReason" placeholder="理由(可选)" style="flex: 1.5" clearable />
                <t-button theme="danger" @click="handleAddBanPlayer()"
                  ><template #icon><add-icon /></template> 封禁</t-button
                >
              </div>
              <t-list v-if="bannedPlayers.length > 0" :split="true" class="custom-list">
                <t-list-item v-for="player in bannedPlayers" :key="player.uuid" class="custom-list-item">
                  <div class="custom-item-layout">
                    <div class="player-info" style="align-items: flex-start">
                      <t-avatar size="small" shape="round" :image="`https://minotar.net/helm/${player.name}/32.png`" />
                      <div class="info-text">
                        <span class="name" style="color: var(--td-error-color)">{{ player.name }}</span>
                        <span class="sub-text" style="white-space: normal">理由: {{ player.reason }}</span>
                      </div>
                    </div>
                    <div class="player-actions">
                      <t-popconfirm
                        content="确定要解封吗？"
                        theme="warning"
                        @confirm="handleRemoveBanPlayer(player.name)"
                      >
                        <t-button size="small" variant="text" theme="primary"> 解封</t-button>
                      </t-popconfirm>
                    </div>
                  </div>
                </t-list-item>
              </t-list>
              <div v-else class="empty-state">暂无被封禁的玩家</div>
            </div>

            <div v-else>
              <div class="input-toolbar">
                <t-input v-model="inputNewBanIp" placeholder="输入IP地址" style="flex: 1" clearable />
                <t-input v-model="inputNewBanReason" placeholder="理由(可选)" style="flex: 1.5" clearable />
                <t-button theme="danger" @click="handleAddBanIp()"
                  ><template #icon><add-icon /></template> 封禁IP</t-button
                >
              </div>
              <t-list v-if="bannedIps.length > 0" :split="true" class="custom-list">
                <t-list-item v-for="ban in bannedIps" :key="ban.ip" class="custom-list-item">
                  <div class="custom-item-layout">
                    <div class="player-info info-text">
                      <span class="name" style="color: var(--td-error-color)">{{ ban.ip }}</span>
                      <span class="sub-text" style="white-space: normal">理由: {{ ban.reason }}</span>
                    </div>
                    <div class="player-actions">
                      <t-popconfirm content="确定要解封该IP吗？" theme="warning" @confirm="handleRemoveBanIp(ban.ip)">
                        <t-button size="small" variant="text" theme="primary"> 解封</t-button>
                      </t-popconfirm>
                    </div>
                  </div>
                </t-list-item>
              </t-list>
              <div v-else class="empty-state">暂无被封禁的IP</div>
            </div>
          </div>
        </t-tab-panel>

        <t-tab-panel value="whitelist" label="白名单">
          <template #label><usergroup-icon style="margin-right: 4px" /> 白名单</template>
          <div class="panel-content">
            <div class="input-toolbar">
              <t-input v-model="inputNewWhitelist" placeholder="输入玩家ID" @enter="handleAddWhitelist()" clearable />
              <t-button theme="primary" @click="handleAddWhitelist()"
                ><template #icon><add-icon /></template> 添加</t-button
              >
            </div>
            <t-list v-if="whitelist.length > 0" :split="true" class="custom-list">
              <t-list-item v-for="user in whitelist" :key="user.uuid" class="custom-list-item">
                <div class="custom-item-layout">
                  <div class="player-info">
                    <t-avatar shape="round" :image="`https://minotar.net/helm/${user.name}/32.png`" />
                    <span class="name">{{ user.name }}</span>
                  </div>
                  <div class="player-actions">
                    <t-popconfirm content="移出白名单？" theme="danger" @confirm="handleRemoveWhitelist(user.name)">
                      <t-button size="small" variant="text" theme="danger"
                        ><template #icon><delete-icon /></template> 移除</t-button
                      >
                    </t-popconfirm>
                  </div>
                </div>
              </t-list-item>
            </t-list>
            <div v-else class="empty-state">白名单为空</div>
          </div>
        </t-tab-panel>
      </t-tabs>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.dialog-body-container {
  height: 60vh;
  min-height: 400px;
  display: flex;
  flex-direction: column;
}

.global-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--td-component-stroke);
  margin-bottom: 12px;
}

.manager-tabs {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

:deep(.t-tabs__content) {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.panel-content {
  flex: 1;
  height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 16px 4px 16px 0;
  box-sizing: border-box;

  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    background-color: var(--td-scrollbar-color);
    border-radius: 4px;
  }
}

.input-toolbar {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
}

.custom-list {
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

:deep(.custom-list-item .t-list-item__content) {
  width: 100%;
}

.custom-item-layout {
  width: 100%;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
}

.player-info {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
  flex: 1;

  .t-avatar {
    flex-shrink: 0;
  }

  .info-text {
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 2px;
  }

  .name {
    font-weight: 500;
    font-family: var(--td-font-family-sans-serif);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .sub-text {
    font-size: 12px;
    color: var(--td-text-color-secondary);
  }
}

.player-actions {
  flex-shrink: 0;
}

@media (max-width: 640px) {
  .dialog-body-container {
    height: 75vh;
  }
  .input-toolbar {
    flex-direction: column;
  }
  .custom-item-layout {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }
  .player-info {
    width: 100%;
    .name {
      white-space: normal;
      word-break: break-all;
      line-height: 1.4;
    }
  }
  .player-actions {
    width: 100%;
    :deep(.action-space) {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-start;
      gap: 12px 8px !important;
    }
  }
}
</style>
