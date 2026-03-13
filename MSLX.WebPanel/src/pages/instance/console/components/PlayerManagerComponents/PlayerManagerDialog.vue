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
    class="player-manager-dialog"
    @close="handleClose"
  >
    <div class="flex flex-col h-[65vh] min-h-[500px]">

      <div class="flex flex-col gap-4 mb-6 shrink-0">

        <div class="flex justify-between items-center">
          <t-tooltip content="指令模式直接与服务端交互，API模式直接修改配置文件" placement="bottom">
            <t-radio-group v-model="opMode" variant="default-filled" size="small" :disabled="!isRunning" class="!bg-zinc-100 dark:!bg-zinc-800 border border-zinc-200/50 dark:border-zinc-700/50 !rounded-lg p-0.5">
              <t-radio-button value="api">API 模式</t-radio-button>
              <t-radio-button value="command">指令优先</t-radio-button>
            </t-radio-group>
          </t-tooltip>

          <t-button variant="text" theme="primary" size="small" :loading="loading" class="!rounded-md hover:!bg-[var(--color-primary)]/10" @click="fetchCurrentTabData">
            <template #icon><refresh-icon /></template> 刷新数据
          </t-button>
        </div>

        <div class="w-full overflow-x-auto hide-scrollbar pb-1">
          <t-radio-group v-model="activeTab" variant="default-filled" class="flex w-max min-w-full !bg-zinc-100 dark:!bg-zinc-800 border border-zinc-200/50 dark:border-zinc-700/50 !rounded-xl p-1">
            <t-radio-button value="online" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><user-icon size="14px"/> 在线</div></t-radio-button>
            <t-radio-button value="history" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><time-icon size="14px"/> 历史</div></t-radio-button>
            <t-radio-button value="ops" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><secured-icon size="14px"/> 管理员</div></t-radio-button>
            <t-radio-button value="banned" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><close-circle-icon size="14px"/> 黑名单</div></t-radio-button>
            <t-radio-button value="whitelist" class="flex-1 !text-center"><div class="flex justify-center items-center gap-1.5"><usergroup-icon size="14px"/> 白名单</div></t-radio-button>
          </t-radio-group>
        </div>
      </div>

      <div class="flex-1 overflow-y-auto custom-scrollbar pr-2 pb-2">

        <div v-if="activeTab === 'online'" class="flex flex-col gap-3">
          <template v-if="onlinePlayers.length > 0">
            <div v-for="player in onlinePlayers" :key="player" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 hover:border-[var(--color-primary)]/30 transition-colors shadow-sm">
              <div class="flex items-center gap-3">
                <img :src="`https://minotar.net/helm/${player}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated]" />
                <span class="font-bold text-sm text-[var(--td-text-color-primary)]">{{ player }}</span>
              </div>
              <div class="flex flex-wrap items-center gap-1.5">
                <t-button size="small" variant="outline" theme="default" class="!rounded-lg !border-zinc-200 dark:!border-zinc-700 !text-zinc-600 dark:!text-zinc-300 hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)]/50" @click="handleAddOp(player)">设为 OP</t-button>
                <t-button size="small" variant="text" theme="warning" class="!rounded-lg hover:!bg-amber-500/10" @click="handleRemoveOp(player)">撤销 OP</t-button>
                <t-button size="small" variant="text" theme="success" class="!rounded-lg hover:!bg-emerald-500/10" @click="handleAddWhitelist(player)">加白</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="sendCmdOnly(`kick ${player} 被管理员踢出`, `已踢出 ${player}`)">踢出</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="handleAddBanPlayer(player)">封禁</t-button>
              </div>
            </div>
          </template>
          <div v-else class="py-16 flex flex-col items-center justify-center text-[var(--td-text-color-secondary)]">
            <user-clear-icon size="40px" class="mb-3 opacity-60" />
            <span class="text-sm font-medium">{{ isRunning ? '当前没有玩家在线' : '服务器未运行' }}</span>
          </div>
        </div>

        <div v-if="activeTab === 'history'" class="flex flex-col gap-3">
          <template v-if="historyPlayers.length > 0">
            <div v-for="user in historyPlayers" :key="user.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 hover:border-[var(--color-primary)]/30 transition-colors shadow-sm">
              <div class="flex items-center gap-3 min-w-0">
                <img :src="`https://minotar.net/helm/${user.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated] shrink-0" />
                <div class="flex flex-col min-w-0">
                  <span class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">{{ user.name }}</span>
                  <span class="text-[11px] text-zinc-500 font-mono truncate mt-0.5">UUID: {{ user.uuid.split('-')[0] }}...</span>
                </div>
              </div>
              <div class="flex flex-wrap items-center gap-1.5 shrink-0">
                <t-button size="small" variant="outline" theme="default" class="!rounded-lg !border-zinc-200 dark:!border-zinc-700 !text-zinc-600 dark:!text-zinc-300 hover:!text-[var(--color-primary)] hover:!border-[var(--color-primary)]/50" @click="handleAddOp(user.name)">设为 OP</t-button>
                <t-button size="small" variant="text" theme="success" class="!rounded-lg hover:!bg-emerald-500/10" @click="handleAddWhitelist(user.name)">加白名单</t-button>
                <t-button size="small" variant="text" theme="danger" class="!rounded-lg hover:!bg-red-500/10" @click="handleAddBanPlayer(user.name)">封禁</t-button>
              </div>
            </div>
          </template>
          <div v-else class="py-16 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">无历史登录记录</div>
        </div>

        <div v-if="activeTab === 'ops'" class="flex flex-col gap-3">
          <div class="flex flex-col sm:flex-row gap-2 mb-2">
            <t-input v-model="inputNewOp" placeholder="输入玩家游戏ID" @enter="handleAddOp()" clearable class="!flex-1" />
            <t-button theme="primary" @click="handleAddOp()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 添加管理员</t-button>
          </div>

          <template v-if="ops.length > 0">
            <div v-for="op in ops" :key="op.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm">
              <div class="flex items-center gap-3">
                <img :src="`https://minotar.net/helm/${op.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated]" />
                <div class="flex flex-col gap-1">
                  <span class="font-bold text-sm text-[var(--td-text-color-primary)]">{{ op.name }}</span>
                  <span class="text-[10px] font-extrabold bg-blue-50 text-blue-600 ring-1 ring-inset ring-blue-500/20 dark:bg-blue-500/10 dark:text-blue-400 dark:ring-blue-500/30 px-1.5 py-0.5 rounded w-max">LV.{{ op.level }}</span>
                </div>
              </div>
              <t-popconfirm content="确定要撤销该管理员吗？" theme="danger" @confirm="handleRemoveOp(op.name)">
                <t-button size="small" variant="outline" theme="danger" class="!rounded-lg !border-red-500/30 hover:!bg-red-500/10 self-start sm:self-auto"><template #icon><delete-icon /></template> 移除</t-button>
              </t-popconfirm>
            </div>
          </template>
          <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无管理员记录</div>
        </div>

        <div v-if="activeTab === 'banned'" class="flex flex-col gap-3">
          <div class="mb-2">
            <t-radio-group v-model="banType" variant="default-filled" size="small" class="!bg-zinc-100 dark:!bg-zinc-800 border border-zinc-200/50 dark:border-zinc-700/50 !rounded-lg p-0.5">
              <t-radio-button value="player">玩家封禁</t-radio-button>
              <t-radio-button value="ip">IP 封禁</t-radio-button>
            </t-radio-group>
          </div>

          <div v-if="banType === 'player'" class="flex flex-col gap-3">
            <div class="flex flex-col sm:flex-row gap-2">
              <t-input v-model="inputNewBanPlayer" placeholder="输入玩家ID" clearable class="!flex-1" />
              <t-input v-model="inputNewBanReason" placeholder="封禁理由(可选)" clearable class="!flex-[1.5]" />
              <t-button theme="danger" @click="handleAddBanPlayer()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 封禁</t-button>
            </div>

            <template v-if="bannedPlayers.length > 0">
              <div v-for="player in bannedPlayers" :key="player.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-red-50/50 dark:bg-red-950/20 rounded-xl border border-red-200/60 dark:border-red-900/40 shadow-sm">
                <div class="flex items-start sm:items-center gap-3 min-w-0">
                  <img :src="`https://minotar.net/helm/${player.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated] shrink-0" />
                  <div class="flex flex-col min-w-0 gap-0.5">
                    <span class="font-bold text-sm text-red-600 dark:text-red-400 truncate">{{ player.name }}</span>
                    <span class="text-[11px] text-[var(--td-text-color-secondary)] mt-0.5 break-all line-clamp-2">理由: {{ player.reason }}</span>
                  </div>
                </div>
                <t-popconfirm content="确定要解封吗？" theme="warning" @confirm="handleRemoveBanPlayer(player.name)">
                  <t-button size="small" variant="outline" theme="primary" class="!rounded-lg !border-[var(--color-primary)]/30 hover:!bg-[var(--color-primary)]/10 shrink-0 self-end sm:self-auto">解封</t-button>
                </t-popconfirm>
              </div>
            </template>
            <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无被封禁的玩家</div>
          </div>

          <div v-else class="flex flex-col gap-3">
            <div class="flex flex-col sm:flex-row gap-2">
              <t-input v-model="inputNewBanIp" placeholder="输入IP地址" clearable class="!flex-1" />
              <t-input v-model="inputNewBanReason" placeholder="封禁理由(可选)" clearable class="!flex-[1.5]" />
              <t-button theme="danger" @click="handleAddBanIp()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 封禁IP</t-button>
            </div>

            <template v-if="bannedIps.length > 0">
              <div v-for="ban in bannedIps" :key="ban.ip" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-red-50/50 dark:bg-red-950/20 rounded-xl border border-red-200/60 dark:border-red-900/40 shadow-sm">
                <div class="flex flex-col min-w-0 gap-0.5">
                  <span class="font-mono font-bold text-sm text-red-600 dark:text-red-400 truncate">{{ ban.ip }}</span>
                  <span class="text-[11px] text-[var(--td-text-color-secondary)] break-all line-clamp-2">理由: {{ ban.reason }}</span>
                </div>
                <t-popconfirm content="确定要解封该IP吗？" theme="warning" @confirm="handleRemoveBanIp(ban.ip)">
                  <t-button size="small" variant="outline" theme="primary" class="!rounded-lg !border-[var(--color-primary)]/30 hover:!bg-[var(--color-primary)]/10 shrink-0 self-end sm:self-auto">解封</t-button>
                </t-popconfirm>
              </div>
            </template>
            <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">暂无被封禁的IP</div>
          </div>
        </div>

        <div v-if="activeTab === 'whitelist'" class="flex flex-col gap-3">
          <div class="flex flex-col sm:flex-row gap-2 mb-2">
            <t-input v-model="inputNewWhitelist" placeholder="输入玩家ID" @enter="handleAddWhitelist()" clearable class="!flex-1" />
            <t-button theme="primary" @click="handleAddWhitelist()" class="!rounded-lg shadow-sm shrink-0"><template #icon><add-icon /></template> 添加白名单</t-button>
          </div>

          <template v-if="whitelist.length > 0">
            <div v-for="user in whitelist" :key="user.uuid" class="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3.5 bg-zinc-50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm">
              <div class="flex items-center gap-3 min-w-0">
                <img :src="`https://minotar.net/helm/${user.name}/32.png`" class="w-9 h-9 rounded shadow-sm [image-rendering:pixelated] shrink-0" />
                <span class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">{{ user.name }}</span>
              </div>
              <t-popconfirm content="移出白名单？" theme="danger" @confirm="handleRemoveWhitelist(user.name)">
                <t-button size="small" variant="outline" theme="danger" class="!rounded-lg !border-red-500/30 hover:!bg-red-500/10 self-start sm:self-auto"><template #icon><delete-icon /></template> 移除</t-button>
              </t-popconfirm>
            </div>
          </template>
          <div v-else class="py-12 flex items-center justify-center text-sm font-medium text-[var(--td-text-color-secondary)]">白名单为空</div>
        </div>

      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

.hide-scrollbar {
  scrollbar-width: none;
  -ms-overflow-style: none;
  &::-webkit-scrollbar {
    display: none;
  }
}

.custom-scrollbar {
  .scrollbar-mixin();
}
</style>
