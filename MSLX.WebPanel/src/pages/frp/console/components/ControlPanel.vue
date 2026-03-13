<script setup lang="ts">
import {
  CloudIcon,
  CodeIcon,
  InternetIcon,
  LinkIcon,
  PlayCircleIcon,
  RefreshIcon,
  ServerIcon,
  StopCircleIcon,
  Edit1Icon,
} from 'tdesign-icons-vue-next';
import { TunnelInfoModel } from '@/api/model/frp';
import { copyText } from '@/utils/clipboard';
import { useUserStore } from '@/store';

const userStore = useUserStore();

// 定义 Props
defineProps<{
  frpId: number;
  isRunning: boolean;
  loading: boolean;
  tunnelInfo: TunnelInfoModel | null;
}>();

// 定义 Emits
defineEmits<{
  start: [];
  stop: [];
  'clear-log': [];
  'edit-config': [];
}>();
</script>

<template>
  <div class="flex flex-col gap-5">

    <div class="design-card bg-[var(--td-bg-color-container)]/80 rounded-xl border border-[var(--td-component-border)] shadow-sm p-5">

      <div class="flex justify-between items-center mb-5">
        <div class="flex items-center gap-2 font-bold text-sm" :class="isRunning ? 'text-[var(--color-success)]' : 'text-zinc-500'">
          <span class="relative flex h-2.5 w-2.5">
            <span v-if="isRunning" class="animate-ping absolute inline-flex h-full w-full rounded-full bg-[var(--color-success)] opacity-75"></span>
            <span class="relative inline-flex rounded-full h-2.5 w-2.5" :class="isRunning ? 'bg-[var(--color-success)]' : 'bg-zinc-400 dark:bg-zinc-600'"></span>
          </span>
          {{ isRunning ? '运行中' : '未运行' }}
        </div>
        <t-tag :theme="isRunning ? 'success' : 'default'" variant="light" class="!rounded !font-bold">
          {{ isRunning ? '状态正常' : '已停止' }}
        </t-tag>
      </div>

      <div class="flex flex-col gap-3">
        <t-button v-if="!isRunning" theme="primary" block :loading="loading" class="!rounded-lg !h-10 !font-bold shadow-sm" @click="$emit('start')">
          <template #icon><play-circle-icon /></template>启动服务
        </t-button>
        <t-button v-else theme="danger" block :loading="loading" class="!rounded-lg !h-10 !font-bold shadow-sm" @click="$emit('stop')">
          <template #icon><stop-circle-icon /></template>停止服务
        </t-button>

        <div class="flex gap-3 w-full mt-2">
          <t-button variant="outline" theme="warning" class="flex-1 !rounded-lg !h-8 !bg-amber-500/10 !border-amber-500/30 !text-amber-600 dark:!text-amber-400 hover:!bg-amber-500/20" @click="$emit('clear-log')">
            <template #icon><refresh-icon /></template>清空日志
          </t-button>
          <t-button v-if="userStore.isAdmin" variant="outline" theme="default" class="flex-1 !rounded-lg !h-8 !bg-zinc-100 dark:!bg-zinc-800 !border-zinc-200 dark:!border-zinc-700 !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700" @click="$emit('edit-config')">
            <template #icon><edit1-icon /></template>配置文件
          </t-button>
        </div>
      </div>
    </div>

    <div class="design-card flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-xl border border-[var(--td-component-border)] shadow-sm p-5">

      <div class="flex justify-between items-center mb-4 pb-4 border-b border-zinc-200/60 dark:border-zinc-700/60">
        <h3 class="text-sm font-bold text-[var(--td-text-color-primary)] m-0">隧道概览</h3>
        <t-tag v-if="tunnelInfo?.proxies?.some((proxy) => proxy.type === 'xtcp')" variant="light-outline" theme="primary" class="!rounded !font-bold">联机房间 - 房主</t-tag>
        <t-tag v-else-if="tunnelInfo?.proxies?.some((proxy) => proxy.type === 'xtcp - Visitors')" variant="light-outline" theme="primary" class="!rounded !font-bold">联机房间 - 访客</t-tag>
        <t-button v-else shape="circle" variant="text" size="small" class="!text-zinc-400 hover:!text-[var(--color-primary)]"><code-icon size="14px" /></t-button>
      </div>

      <div class="flex flex-col">
        <div class="flex justify-between items-center py-2">
          <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><server-icon size="14px" /> 隧道实例 ID</div>
          <div class="font-mono font-bold text-sm text-[var(--td-text-color-primary)]">#{{ frpId }}</div>
        </div>

        <template v-if="tunnelInfo?.proxies?.length > 0">
          <div v-for="(proxy, index) in tunnelInfo.proxies" :key="index" class="flex flex-col gap-2 pt-4 mt-3 border-t border-dashed border-zinc-200 dark:border-zinc-700/60">
            <div v-if="tunnelInfo.proxies.length > 1" class="text-[11px] font-bold text-zinc-400 mb-1.5">
              配置 #{{ index + 1 }}
            </div>

            <div class="flex justify-between items-center py-1.5">
              <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><cloud-icon size="14px" /> {{ proxy.type.includes('xtcp') ? '房间号' : '名称' }}</div>
              <t-tooltip :content="proxy.proxyName" placement="top" show-arrow destroy-on-close>
                <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate max-w-[140px] cursor-pointer hover:text-[var(--color-primary)] transition-colors" @click="copyText(proxy.proxyName, true, `${proxy.type.includes('xtcp') ? '房间号' : '隧道名称'}已复制！`)">
                  {{ proxy.proxyName }}
                </div>
              </t-tooltip>
            </div>

            <div class="flex justify-between items-center py-1.5">
              <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><internet-icon size="14px" /> 协议</div>
              <div class="text-xs font-bold text-[var(--color-primary)] uppercase">{{ proxy.type }}</div>
            </div>

            <div class="flex justify-between items-center py-1.5">
              <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><link-icon size="14px" /> {{ proxy.type.includes('xtcp') ? '密钥' : '远程地址' }}</div>
              <t-tooltip :content="proxy.remoteAddressMain" placement="top" show-arrow destroy-on-close>
                <div class="font-mono font-bold text-xs text-[var(--td-text-color-primary)] truncate max-w-[140px] cursor-pointer hover:text-[var(--color-primary)] transition-colors" @click="copyText(proxy.remoteAddressMain, true, `${proxy.type.includes('xtcp') ? '房间密钥' : '连接地址'}已复制！`)">
                  {{ proxy.remoteAddressMain || '获取中...' }}
                </div>
              </t-tooltip>
            </div>

            <div v-if="proxy.remoteAddressBackup && proxy.remoteAddressBackup !== proxy.remoteAddressMain" class="flex justify-between items-center py-1.5">
              <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><link-icon size="14px" /> 备用地址</div>
              <t-tooltip :content="proxy.remoteAddressBackup" placement="top" show-arrow destroy-on-close>
                <div class="font-mono font-bold text-xs text-[var(--td-text-color-primary)] truncate max-w-[140px] cursor-pointer hover:text-[var(--color-primary)] transition-colors" @click="copyText(proxy.remoteAddressBackup, true, '备用连接地址已复制！')">
                  {{ proxy.remoteAddressBackup }}
                </div>
              </t-tooltip>
            </div>

            <div class="flex justify-between items-center py-1.5">
              <div class="flex items-center gap-1.5 text-xs text-[var(--td-text-color-secondary)]"><code-icon size="14px" /> 本地地址</div>
              <div class="font-mono text-xs text-[var(--td-text-color-secondary)]">{{ proxy.localAddress }}</div>
            </div>
          </div>
        </template>

        <div v-else class="py-8 text-center flex flex-col items-center justify-center opacity-60">
          <server-icon size="24px" class="text-zinc-400 mb-2" />
          <span class="text-xs font-medium text-zinc-500">{{ loading ? '加载配置中...' : '暂无隧道信息' }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";
</style>
