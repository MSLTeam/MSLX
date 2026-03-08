<script setup lang="ts">
import { computed, ref, onUnmounted } from 'vue';
import {
  DownloadIcon,
  CloseIcon,
  CloudDownloadIcon,
  ErrorCircleIcon,
  CheckCircleIcon,
  StopCircleIcon,
  LinkIcon,
} from 'tdesign-icons-vue-next';
import { HubConnectionBuilder, LogLevel, HubConnection } from '@microsoft/signalr';
import { MessagePlugin } from 'tdesign-vue-next';
import { useUserStore } from '@/store';
import { postUpdateDaemon } from '@/api/update';
import { request } from '@/utils/request';
import { changeUrl } from '@/router';
import { UpdateDownloadInfoModel, UpdateInfoModel } from '@/api/model/update';

// === 接口定义 ===
interface Props {
  visible: boolean;
  updateInfo: UpdateInfoModel | null;
  downloadInfo: UpdateDownloadInfoModel | null;
}

const props = defineProps<Props>();
const emit = defineEmits(['close', 'skip', 'success']);
const userStore = useUserStore();

// === 状态管理 ===
const isUpdating = ref(false);
const updateProgress = ref(0);
const updateSpeed = ref('0 KB/s');
const updateStatusText = ref('准备中...');
const isDockerEnv = ref(false);
const hasRunningServers = ref(false);
const updateSuccess = ref(false);
const errorMessage = ref('');

// SignalR 实例
let hubConnection: HubConnection | null = null;

// === 计算属性 ===
const isBeta = computed(() => props.updateInfo?.status === 'beta');

// 判断是否为 macOS
const isMacOS = computed(() => {
  const osType = userStore.userInfo?.systemInfo?.osType || '';
  return osType.includes('macOS') || osType.includes('OSX');
});

// 判断是否为 Linux
const isLinux = computed(() => {
  const osType = userStore.userInfo?.systemInfo?.osType || '';
  return osType.toLowerCase().includes('linux');
});

// === 方法 ===

// 打开外部链接
const openLink = (url: string) => {
  if (url) window.open(url, '_blank');
};

// 跳转到实例列表
const toInstanceList = () => {
  emit('close'); // 关闭弹窗
  changeUrl('/instance/list'); // 路由跳转
};

// 关闭弹窗
const handleClose = () => {
  if (isUpdating.value && !updateSuccess.value) {
    MessagePlugin.warning('正在更新中，请勿关闭窗口');
    return;
  }
  stopSignalR();
  emit('close');
};

const handleSkip = () => {
  emit('skip');
};

const reloadPage = () => {
  window.location.reload();
};

// === SignalR 逻辑 ===
const stopSignalR = async () => {
  if (hubConnection) {
    try {
      await hubConnection.stop();
    } catch (e) {
      console.error('Stop Hub Error:', e);
    }
    hubConnection = null;
  }
};

const startSignalR = async () => {
  await stopSignalR();

  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/daemonUpdate', baseUrl || window.location.origin);
  if (token) hubUrl.searchParams.append('x-user-token', token);

  hubConnection = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Warning)
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .build();

  // 监听进度事件
  hubConnection.on('UpdateProgress', (data: any) => {
    updateProgress.value = data.progress || 0;
    updateSpeed.value = data.speed || '';

    // macos权限检查状态
    if (data.stage === 'permission_check') {
      updateStatusText.value = '等待服务端确认权限...';
    }
    // 重启状态
    else if (data.stage === 'restarting') {
      console.log('[Update] 收到重启信号，准备轮询...');
      updateStatusText.value = '服务正在重启...';

      stopSignalR();
      setTimeout(() => {
        startPollingPing();
      }, 3000);
    } else {
      // 普通进度状态
      updateStatusText.value = data.status || '正在处理...';
    }
  });

  hubConnection.on('UpdateFailed', (msg: string) => {
    isUpdating.value = false;
    errorMessage.value = msg || '更新失败';
    stopSignalR();
  });

  hubConnection.onclose((error) => {
    if (!hubConnection) return;
    // 进度满了且断开
    if (isUpdating.value && updateProgress.value >= 100) {
      setTimeout(() => {
        startPollingPing();
      }, 6000);
    } else if (error) {
      isUpdating.value = false;
      errorMessage.value = `连接断开: ${error.message}`;
    }
  });

  try {
    await hubConnection.start();
  } catch (err: any) {
    errorMessage.value = `连接更新服务失败: ${err.message}`;
    isUpdating.value = false;
  }
};

// === 发起自动更新 ===
const handleAutoUpdate = async () => {
  if (isUpdating.value) return;

  // 重置所有状态
  isUpdating.value = true;
  isDockerEnv.value = false;
  hasRunningServers.value = false;
  errorMessage.value = '';
  updateProgress.value = 0;
  updateSuccess.value = false;

  await startSignalR();

  try {
    await postUpdateDaemon();
  } catch (error: any) {
    isUpdating.value = false;
    stopSignalR();

    const msg = error.message || '';

    // 检测 Docker 环境
    if (msg.includes('Docker') || msg.includes('容器')) {
      isDockerEnv.value = true;
    }
    // 检测是否有运行中的服务器
    else if (msg.includes('运行') && (msg.includes('服务器') || msg.includes('实例'))) {
      hasRunningServers.value = true;
    } else {
      errorMessage.value = msg || '请求更新失败，请检查网络或日志';
    }
  }
};

// === 轮询 Ping ===
const startPollingPing = async () => {
  updateStatusText.value = '服务正在重启，请稍候...';

  const checkStatus = async () => {
    try {
      await request.get({ url: '/api/ping', timeout: 3000 });
      return true;
    } catch {
      return false;
    }
  };

  const maxRetries = 60;
  let retryCount = 0;

  const timer = setInterval(async () => {
    retryCount++;
    const isOnline = await checkStatus();

    if (isOnline) {
      clearInterval(timer);
      isUpdating.value = false;
      updateSuccess.value = true;
      updateStatusText.value = '更新成功！';
      stopSignalR();
      setTimeout(() => emit('success'), 1000);
    } else if (retryCount > maxRetries) {
      clearInterval(timer);
      isUpdating.value = false;
      errorMessage.value = '服务重启超时，请手动刷新页面检查状态。';
      stopSignalR();
    }
  }, 2000);
};

onUnmounted(() => {
  stopSignalR();
});
</script>
<template>
  <t-dialog
    :visible="props.visible"
    :header="false"
    :footer="false"
    :close-on-overlay-click="false"
    :close-btn="false"
    width="500px"
    class="update-modal"
    destroy-on-close
    attach="body"
    top="2vh"
    @close="handleClose"
  >
    <div class="flex justify-between items-start mb-5">
      <div class="flex flex-col">
        <div class="flex items-center gap-2">
          <h3 class="m-0 text-[20px] font-bold text-zinc-800 dark:text-zinc-100 tracking-wide">
            {{ updateSuccess ? '更新完成' : '发现新版本' }}
          </h3>
          <t-tag v-if="isBeta" theme="warning" variant="light-outline" class="!rounded-md !font-bold">Beta</t-tag>
          <t-tag v-else theme="success" variant="light-outline" class="!rounded-md !font-bold">Release</t-tag>
        </div>

        <div class="mt-2.5 flex items-center gap-2">
          <t-tag variant="outline" size="small" class="!font-mono !rounded-md">{{ updateInfo?.currentVersion }}</t-tag>
          <span class="text-zinc-400 font-mono font-bold">→</span>
          <t-tag theme="primary" variant="light-outline" size="small" class="!font-mono !rounded-md">{{ updateInfo?.latestVersion }}</t-tag>
        </div>
      </div>
      <t-button v-if="!isUpdating" variant="text" shape="circle" class="hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose">
        <template #icon><close-icon /></template>
      </t-button>
    </div>

    <div class="mb-6 min-h-[120px] flex flex-col justify-center">

      <div v-if="updateSuccess" class="flex flex-col items-center text-center py-2">
        <check-circle-icon size="48px" class="text-emerald-500 mb-4 drop-shadow-sm" />
        <p class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0 mb-1">MSLX守护进程端已成功更新</p>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0">请刷新页面以加载最新功能</p>
      </div>

      <div v-else-if="isDockerEnv" class="py-2">
        <t-alert theme="warning" title="检测到 Docker 环境" class="!rounded-xl">
          <template #message>
            当前程序运行在 <b>Docker 容器</b> 内，不支持热更新。<br />请使用以下命令或者参照 <b>官方文档</b> 更新。<br />
            <t-link theme="primary" href="https://mslx.mslmc.cn/docs/install/docker/" target="_blank" class="mt-1 align-baseline">
              <b>Docker安装/更新文档</b>
            </t-link>
          </template>
        </t-alert>
        <div class="mt-3 bg-[#1e1e1e] text-[#d4d4d4] p-3 rounded-xl font-mono text-[13px] break-all select-all shadow-inner border border-black/20">
          sudo docker compose pull && docker compose up -d <span class="text-zinc-500"># 指令仅适用于Compose部署</span>
        </div>
      </div>

      <div v-else-if="hasRunningServers" class="flex flex-col items-center text-center py-4">
        <stop-circle-icon size="48px" class="text-amber-500 mb-3 drop-shadow-sm" />
        <p class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0 mb-2">无法开始更新</p>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0 leading-relaxed">
          检测到当前有服务器实例正在运行。<br />为了防止数据丢失，请先停止所有实例。
        </p>
      </div>

      <div v-else-if="updateStatusText.includes('等待服务端确认权限')" class="flex flex-col items-center text-center py-4">
        <p class="text-base font-bold text-zinc-800 dark:text-zinc-200 m-0 mb-2">请在服务端确认权限</p>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0 leading-relaxed">
          macOS 系统已弹出提示：<br />
          <strong class="text-zinc-700 dark:text-zinc-300">“MSLX-Daemon 想要控制应用程序 终端.app”</strong> <br />
          请务必点击 <strong>【好/OK】</strong> 以继续更新。
        </p>
      </div>

      <div v-else-if="isUpdating || errorMessage" class="py-2">
        <template v-if="errorMessage">
          <div class="flex items-center gap-2 text-red-600 dark:text-red-400 bg-red-50 dark:bg-red-950/30 p-3.5 rounded-xl border border-red-100 dark:border-red-900/50">
            <error-circle-icon class="shrink-0 text-lg" />
            <span class="text-sm font-medium">{{ errorMessage }}</span>
          </div>
        </template>
        <template v-else>
          <div class="flex justify-between items-end mb-2 text-sm">
            <span class="font-medium text-[var(--color-primary)]">{{ updateStatusText }}</span>
            <span class="text-xs font-mono text-zinc-400 dark:text-zinc-500">{{ updateSpeed }}</span>
          </div>
          <t-progress theme="plump" :percentage="updateProgress" :status="updateProgress >= 100 ? 'active' : 'success'" />
        </template>
      </div>

      <div v-else class="flex flex-col gap-3">
        <t-alert v-if="isMacOS" theme="warning" variant="outline" class="!rounded-xl !text-[13px] leading-relaxed">
          <template #message>
            <strong>macOS 用户请注意：</strong><br />
            受 Apple 安全机制 (Gatekeeper) 限制，更新重启后应用可能无法自动启动。如遇此情况，请前往「系统设置 > 隐私与安全性」手动允许应用运行。
          </template>
        </t-alert>

        <t-alert v-if="isLinux" theme="info" variant="outline" class="!rounded-xl !text-[13px] leading-relaxed">
          <template #message>
            <strong>Linux 用户提示：</strong>
            <ul class="m-0 mt-1 pl-4 leading-relaxed opacity-90 space-y-1">
              <li>如使用一键脚本部署，推荐优先参考 <t-link theme="primary" href="https://mslx.mslmc.cn/docs/install/linux/" target="_blank" class="align-baseline font-bold">官方文档</t-link>。</li>
              <li>若启用 <strong>Systemd</strong> 托管，请确保服务名称为 <code>mslx</code>，否则无法自动重启。</li>
              <li>如果更新完成后仍然是旧版本，请尝试手动重启服务或手动更新！</li>
            </ul>
          </template>
        </t-alert>

        <div class="flex flex-col gap-1.5 mt-1">
          <div class="text-[13px] font-bold text-zinc-600 dark:text-zinc-400 tracking-wider">更新内容</div>
          <div class="bg-zinc-50 dark:bg-zinc-900/50 rounded-xl p-3.5 max-h-[200px] overflow-y-auto border border-zinc-200/60 dark:border-zinc-700/50 shadow-inner custom-scrollbar">
            <div class="font-mono text-[13px] leading-relaxed whitespace-pre-wrap text-zinc-700 dark:text-zinc-300">
              {{ updateInfo?.log || '暂无详细日志' }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="!updateSuccess && !isDockerEnv && !hasRunningServers" class="flex flex-col gap-3">
      <t-button theme="primary" block size="large" :loading="isUpdating" :disabled="isUpdating" class="!rounded-xl shadow-sm" @click="handleAutoUpdate">
        <template #icon><cloud-download-icon /></template>
        {{ isUpdating ? '正在更新...' : '立即更新' }}
      </t-button>

      <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <t-button variant="outline" block class="!rounded-xl !m-0" :disabled="!downloadInfo?.file || isUpdating" @click="openLink(downloadInfo?.file || '')">
          <template #icon><download-icon /></template> 下载新版本
        </t-button>
        <t-button variant="dashed" block class="!rounded-xl !m-0" :disabled="!downloadInfo?.web || isUpdating" @click="openLink(downloadInfo?.web || '')">
          <template #icon><link-icon /></template> 前往下载页
        </t-button>
      </div>

      <div v-if="!isUpdating" class="mt-2 flex justify-center">
        <t-popconfirm content="确定要跳过此版本吗？跳过后将不再提示该版本。后续可在设置中更新。" theme="warning" @confirm="handleSkip">
          <t-link theme="default" hover="color" size="small" class="!text-zinc-400 hover:!text-zinc-600 dark:hover:!text-zinc-300">
            跳过此版本
          </t-link>
        </t-popconfirm>
      </div>
    </div>

    <div v-if="updateSuccess" class="mt-4">
      <t-button theme="primary" size="large" block class="!rounded-xl shadow-sm" @click="reloadPage">刷新页面</t-button>
    </div>

    <div v-if="isDockerEnv" class="mt-4">
      <t-button variant="outline" size="large" block class="!rounded-xl" @click="handleClose">我知道了</t-button>
    </div>

    <div v-if="hasRunningServers" class="flex flex-col gap-3 mt-4">
      <t-button theme="primary" size="large" block class="!rounded-xl shadow-sm" @click="toInstanceList">前往实例列表管理</t-button>
      <t-button variant="outline" size="large" block class="!rounded-xl !m-0" @click="handleClose">暂不更新</t-button>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

:deep(.update-modal) {
  @media (max-width: 768px) {
    width: 90vw !important;
    max-width: 400px;
  }
}

/* 日志框的极简滚动条 */
.custom-scrollbar {
  scrollbar-width: thin;
  scrollbar-color: var(--td-scrollbar-color) transparent;

  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    background: var(--td-scrollbar-color);
    border-radius: 4px;
  }
}
</style>
