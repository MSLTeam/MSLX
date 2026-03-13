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
  <div class="design-card flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-5">

    <div class="flex justify-between items-center mb-4 pb-4 border-b border-zinc-200/60 dark:border-zinc-700/60">
      <div class="flex items-center gap-1.5 font-bold text-sm text-[var(--td-text-color-primary)] m-0">
        <usergroup-icon size="16px" class="text-[var(--td-text-color-secondary)]" />
        在线玩家
        <span v-if="status === 2" class="text-xs font-medium text-[var(--td-text-color-secondary)]">({{ onlinePlayers.length }})</span>
      </div>

      <t-button size="small" variant="text" theme="primary" class="!rounded-md hover:!bg-[var(--color-primary)]/10 transition-colors" @click="showManager = true" :disabled="status === 0">
        <template #icon><setting-icon /></template>管理
      </t-button>
    </div>

    <div class="flex-1 min-h-[40px]">
      <template v-if="status === 2">
        <div v-if="onlinePlayers.length > 0" class="flex flex-wrap gap-2">
          <t-dropdown
            v-for="player in onlinePlayers"
            :key="player"
            :options="actionOptions as any"
            trigger="click"
            placement="bottom-left"
            @click="(item) => handleDropdownClick(item, player)"
          >
            <div class="flex items-center gap-1.5 px-2 py-1 bg-zinc-100 dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 hover:border-[var(--color-primary)]/50 hover:bg-[var(--color-primary)]/5 rounded-md cursor-pointer transition-colors text-xs font-bold text-zinc-700 dark:text-zinc-300 shadow-sm">
              <img :src="`https://minotar.net/helm/${player}/16.png`" class="w-3.5 h-3.5 rounded-[2px] shadow-sm [image-rendering:pixelated]" />
              {{ player }}
            </div>
          </t-dropdown>
        </div>

        <div v-else class="py-4 text-center text-xs font-medium text-[var(--td-text-color-secondary)]">当前无人在线</div>
      </template>

      <div v-else class="py-4 text-center text-xs font-medium text-[var(--td-text-color-secondary)]">服务器未运行</div>
    </div>

    <player-manager-dialog v-model:visible="showManager" :server-id="serverId" :is-running="status === 2" />
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
