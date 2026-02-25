<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { useInstanceHubStore } from '@/store/modules/instanceHub';
import { getOnlinePlayers } from '@/api/instance';
import { UsergroupIcon, SettingIcon } from 'tdesign-icons-vue-next';
import PlayerManagerDialog from './PlayerManagerDialog.vue';

const props = defineProps<{
  serverId: number;
  status: number; // 2 = 运行中
}>();

const hubStore = useInstanceHubStore();
const onlinePlayers = ref<string[]>([]);
const showManager = ref(false);

// SignalR 取消订阅引用
let unSubJoined: (() => void) | null = null;
let unSubLeft: (() => void) | null = null;
let unSubCleared: (() => void) | null = null;

const actionOptions = [
  { content: '设为管理员', value: 'op' },
  { content: '取消管理员', value: 'deop' },
  { content: '踢出服务器', value: 'kick', theme: 'warning' },
  { content: '封禁玩家', value: 'ban', theme: 'error' },
  { content: '加入白名单', value: 'whitelist add' },
];

// 初始化拉取在线玩家
const fetchInitialPlayers = async () => {
  if (props.status !== 2) {
    onlinePlayers.value = [];
    return;
  }
  try {
    onlinePlayers.value = await getOnlinePlayers(props.serverId);
  } catch (error) {
    console.error('拉取在线玩家失败:', error);
  }
};

// 绑定 SignalR 动态事件
const bindSignalR = () => {
  unSubJoined = hubStore.onPlayerJoined?.((name: string) => {
    if (!onlinePlayers.value.includes(name)) {
      onlinePlayers.value.push(name);
    }
  });

  unSubLeft = hubStore.onPlayerLeft?.((name: string) => {
    onlinePlayers.value = onlinePlayers.value.filter((p) => p !== name);
  });

  unSubCleared = hubStore.onPlayerListCleared?.(() => {
    onlinePlayers.value = [];
  });
};

const unbindSignalR = () => {
  unSubJoined?.();
  unSubLeft?.();
  unSubCleared?.();
};

// 监听状态变化
watch(
  () => props.status,
  (newStatus) => {
    if (newStatus === 2) {
      fetchInitialPlayers();
    } else {
      onlinePlayers.value = [];
    }
  },
);

watch(
  () => props.serverId,
  () => {
    fetchInitialPlayers();
  },
);

onMounted(() => {
  fetchInitialPlayers();
  bindSignalR();
});

onUnmounted(() => {
  unbindSignalR();
});

// 处理菜单点击
const handleDropdownClick = async (dropdownItem: any, playerName: string) => {
  const cmdPrefix = dropdownItem.value;
  const reason =
    cmdPrefix === 'kick' || cmdPrefix === 'ban'
      ? cmdPrefix === 'kick'
        ? ' 您被控制台踢出了服务器'
        : '您被控制台封禁了'
      : '';
  const fullCmd = `${cmdPrefix} ${playerName}${reason}`;

  try {
    await hubStore.sendCommand(fullCmd);
    switch (cmdPrefix) {
      case 'kick':
        MessagePlugin.success(`已将 ${playerName} 踢出服务器`);
        break;
      case 'ban':
        MessagePlugin.success(`已将 ${playerName} 封禁`);
        break;
      case 'op':
        MessagePlugin.success(`已将 ${playerName} 设置为服务器管理员`);
        break;
      case 'deop':
        MessagePlugin.success(`已取消 ${playerName} 为服务器管理员`);
        break;
      case 'whitelist add':
        MessagePlugin.success(`已将 ${playerName} 添加到白名单`);
        break;
      default:
        MessagePlugin.success(`针对 ${playerName} 的指令已发送`);
        break;
    }
  } catch (error: any) {
    MessagePlugin.error(`执行失败: ${error.message}`);
  }
};
</script>

<template>
  <t-card :bordered="false" class="player-list-card">
    <div class="header-row">
      <div class="title">
        <usergroup-icon /> 在线玩家 <span class="count" v-if="status === 2">({{ onlinePlayers.length }})</span>
      </div>
      <t-button size="small" variant="text" theme="primary" @click="showManager = true" :disabled="status === 0">
        <template #icon><setting-icon /></template>管理
      </t-button>
    </div>

    <div class="content-area">
      <template v-if="status === 2">
        <t-space v-if="onlinePlayers.length > 0" break-line size="small">
          <t-dropdown
            v-for="player in onlinePlayers"
            :key="player"
            :options="actionOptions as any"
            trigger="click"
            placement="bottom-left"
            @click="(item) => handleDropdownClick(item, player)"
          >
            <t-tag class="player-tag" variant="light" hoverable>
              <template #icon>
                <img :src="`https://minotar.net/helm/${player}/16.png`" class="player-avatar" />
              </template>
              {{ player }}
            </t-tag>
          </t-dropdown>
        </t-space>
        <div v-else class="empty-text">当前无人在线</div>
      </template>
      <div v-else class="empty-text">服务器未运行</div>
    </div>

    <player-manager-dialog v-model:visible="showManager" :server-id="serverId" :is-running="status === 2" />
  </t-card>
</template>

<style scoped lang="less">
.player-list-card {
  :deep(.t-card__body) {
    padding: 16px;
  }

  .header-row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 12px;

    .title {
      font-size: 14px;
      font-weight: 600;
      color: var(--td-text-color-primary);
      display: flex;
      align-items: center;
      gap: 6px;

      .count {
        color: var(--td-text-color-placeholder);
        font-size: 13px;
        font-weight: normal;
      }
    }
  }

  .content-area {
    min-height: 40px;

    .player-tag {
      cursor: pointer;
      border: 1px solid transparent;
      transition: all 0.2s;

      &:hover {
        border-color: var(--td-brand-color);
        background-color: color-mix(in srgb, var(--td-brand-color), transparent 90%);
      }

      .player-avatar {
        width: 14px;
        height: 14px;
        border-radius: 2px;
        margin-right: 4px;
        image-rendering: pixelated;
      }
    }

    .empty-text {
      color: var(--td-text-color-placeholder);
      font-size: 13px;
      text-align: center;
      padding: 10px 0;
    }
  }
}
</style>
