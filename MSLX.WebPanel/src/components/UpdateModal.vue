<script setup lang="ts">
import { computed, ref, onUnmounted } from 'vue';
import {
  DownloadIcon,
  BrowseIcon,
  CloseIcon,
  CloudDownloadIcon,
  ErrorCircleIcon,
  CheckCircleIcon,
  StopCircleIcon,
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

    // === 新增：权限检查状态 ===
    if (data.stage === 'permission_check') {
      // 此时不要断开连接，而是显示提示
      updateStatusText.value = '等待服务端确认权限...';
      // 你可以在这里加一个特殊的标志位，让 UI 显示那个 macOS 的 Alert
      // 例如：isWaitingPermission.value = true;
    }
    // === 原有：重启状态 ===
    else if (data.stage === 'restarting') {
      console.log('[Update] 收到重启信号，准备轮询...');
      updateStatusText.value = '服务正在重启...';

      stopSignalR();

      // 现在后端确保了脚本启动后才发信号，这里的延时可以适当缩短，或者保留以求稳
      setTimeout(() => {
        startPollingPing();
      }, 3000);
    }
    else {
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
    @close="handleClose"
  >
    <div class="modal-header">
      <div class="header-content">
        <div class="title-row">
          <h3>{{ updateSuccess ? '更新完成' : '发现新版本' }}</h3>
          <t-tag v-if="isBeta" theme="warning" variant="light">Beta</t-tag>
          <t-tag v-else theme="success" variant="light">Release</t-tag>
        </div>
        <div class="version-row">
          <t-tag variant="outline" size="small">{{ updateInfo?.currentVersion }}</t-tag>
          <span class="arrow">→</span>
          <t-tag theme="primary" variant="light-outline" size="small">{{ updateInfo?.latestVersion }}</t-tag>
        </div>
      </div>
      <t-button v-if="!isUpdating" variant="text" shape="circle" @click="handleClose">
        <template #icon><close-icon /></template>
      </t-button>
    </div>

    <div class="modal-body">
      <div v-if="updateSuccess" class="status-container success">
        <check-circle-icon size="48px" class="success-icon" />
        <p><b>MSLX守护进程端</b>已成功更新至最新版本</p>
        <p class="sub-text">请刷新页面以加载最新功能</p>
      </div>

      <div v-else-if="isDockerEnv" class="status-container docker-warn">
        <t-alert
          theme="warning"
          title="检测到 Docker 环境"
          message="当前程序运行在 Docker 容器内，不支持热更新。请使用以下命令更新："
        />
        <div class="code-block">docker pull docker.mslmc.cn/xiaoyululu/mslx-daemon:latest && docker-compose up -d</div>
      </div>

      <div v-else-if="hasRunningServers" class="status-container server-running-warn">
        <div class="warn-icon-wrapper">
          <stop-circle-icon size="48px" style="color: var(--td-warning-color)" />
        </div>
        <p class="warn-title">无法开始更新</p>
        <p class="warn-desc">
          检测到当前有服务器实例正在运行。
          <br />为了防止数据丢失，请先停止所有实例。
        </p>
      </div>

      <div v-else-if="updateStatusText.includes('等待服务端确认权限')" class="status-container permission-wait">
        <div class="warn-icon-wrapper">
          <t-loading size="40px" text="等待中..." />
        </div>
        <p class="warn-title">请在服务端确认权限</p>
        <p class="warn-desc">
          macOS 系统已弹出提示：
          <br><strong>“MSLX-Daemon 想要控制应用程序 终端.app”</strong>
          <br>请务必点击 <strong>【好/OK】</strong> 以继续更新。
        </p>
      </div>

      <div v-else-if="isUpdating || errorMessage" class="status-container updating">
        <template v-if="errorMessage">
          <div class="error-box">
            <error-circle-icon style="color: var(--td-error-color)" />
            <span>{{ errorMessage }}</span>
          </div>
        </template>
        <template v-else>
          <div class="progress-info">
            <span>{{ updateStatusText }}</span>
            <span class="speed">{{ updateSpeed }}</span>
          </div>
          <t-progress
            theme="plump"
            :percentage="updateProgress"
            :status="updateProgress >= 100 ? 'active' : 'success'"
          />
        </template>
      </div>

      <div v-else>
        <t-alert v-if="isMacOS" theme="warning" variant="outline" class="macos-alert">
          <template #message>
            <strong>macOS 用户请注意：</strong>
            <br />受 Apple 安全机制 (Gatekeeper) 限制，更新重启后应用可能无法自动启动。如遇此情况，请前往「系统设置 >
            隐私与安全性」手动允许应用运行。
          </template>
        </t-alert>

        <t-alert v-if="isLinux" theme="info" variant="outline" class="macos-alert">
          <template #message>
            <strong>Linux 用户提示：</strong>
            <ul style="margin: 4px 0 0 0; line-height: 1.6;">
              <li>
                如使用一键脚本部署，推荐优先参考
                <t-link theme="primary" href="https://mslx.mslmc.cn/docs/install/linux/" target="_blank" style="vertical-align: baseline;">
                  官方文档
                </t-link>
                使用脚本进行更新。
              </li>
              <li>
                若启用 <strong>Systemd</strong> 托管，请确保服务名称为 <code>mslx</code>，否则自动更新后可能无法自动重启。
              </li>
              <li>
                如果更新完成后仍然是旧版本，请尝试手动重启服务或手动更新！
              </li>
            </ul>
          </template>
        </t-alert>

        <div class="log-title">更新内容：</div>
        <div class="log-scroll-area">
          <div class="log-text">{{ updateInfo?.log || '暂无详细日志' }}</div>
        </div>
      </div>
    </div>

    <div v-if="!updateSuccess && !isDockerEnv && !hasRunningServers" class="modal-footer">
      <div class="primary-actions">
        <t-button theme="primary" block :loading="isUpdating" :disabled="isUpdating" @click="handleAutoUpdate">
          <template #icon><cloud-download-icon /></template>
          {{ isUpdating ? '正在更新...' : '立即更新' }}
        </t-button>

        <div class="secondary-grid">
          <t-button
            variant="outline"
            :disabled="!downloadInfo?.file || isUpdating"
            @click="openLink(downloadInfo?.file || '')"
          >
            <template #icon><download-icon /></template>
            下载新版本
          </t-button>

          <t-button
            variant="dashed"
            :disabled="!downloadInfo?.web || isUpdating"
            @click="openLink(downloadInfo?.web || '')"
          >
            <template #icon><browse-icon /></template>
            前往下载页
          </t-button>
        </div>
      </div>

      <div v-if="!isUpdating" class="sub-actions">
        <t-link theme="default" hover="color" size="small" @click="handleSkip"> 跳过此版本 </t-link>
      </div>
    </div>

    <div v-if="updateSuccess" class="modal-footer">
      <t-button theme="primary" block @click="reloadPage">刷新页面</t-button>
    </div>

    <div v-if="isDockerEnv" class="modal-footer">
      <t-button variant="outline" block @click="handleClose">我知道了</t-button>
    </div>

    <div v-if="hasRunningServers" class="modal-footer">
      <div class="primary-actions">
        <t-button theme="primary" block @click="toInstanceList"> 前往实例列表管理 </t-button>
        <t-button variant="outline" style="margin-left: 0" block @click="handleClose"> 暂不更新 </t-button>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.update-modal {
  @media (max-width: 768px) {
    width: 90% !important;
    max-width: 400px;
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;

  .header-content {
    .title-row {
      display: flex;
      align-items: center;
      gap: 8px;
      h3 {
        margin: 0;
        font-size: 20px;
        font-weight: 600;
        color: var(--td-text-color-primary);
      }
    }
    .version-row {
      margin-top: 8px;
      display: flex;
      align-items: center;
      gap: 8px;
      .arrow {
        color: var(--td-text-color-placeholder);
        font-family: monospace;
      }
    }
  }
}

.modal-body {
  margin-bottom: 24px;
  min-height: 120px;
  display: flex;
  flex-direction: column;
  justify-content: center;

  .macos-alert {
    margin-bottom: 16px;
    font-size: 13px;
    line-height: 1.5;
  }

  .status-container {
    padding: 10px 0;

    &.success {
      text-align: center;
      .success-icon {
        color: var(--td-success-color);
        margin-bottom: 16px;
      }
      p {
        font-size: 16px;
        font-weight: 600;
        margin: 0 0 4px 0;
      }
      .sub-text {
        font-size: 14px;
        color: var(--td-text-color-secondary);
        font-weight: normal;
      }
    }

    // 新增：服务器运行警告样式
    &.server-running-warn {
      text-align: center;
      .warn-icon-wrapper {
        margin-bottom: 12px;
      }
      .warn-title {
        font-size: 16px;
        font-weight: 600;
        margin-bottom: 8px;
        color: var(--td-text-color-primary);
      }
      .warn-desc {
        font-size: 14px;
        color: var(--td-text-color-secondary);
        line-height: 1.6;
      }
    }

    &.updating {
      .progress-info {
        display: flex;
        justify-content: space-between;
        margin-bottom: 8px;
        font-size: 14px;
        color: var(--td-text-color-primary);
        .speed {
          color: var(--td-text-color-secondary);
        }
      }
      .error-box {
        display: flex;
        align-items: center;
        gap: 8px;
        color: var(--td-error-color);
        background: var(--td-error-color-1);
        padding: 12px;
        border-radius: var(--td-radius-medium);
      }
    }

    &.docker-warn {
      .code-block {
        margin-top: 12px;
        background: #1e1e1e;
        color: #d4d4d4;
        padding: 10px;
        border-radius: 4px;
        font-family: monospace;
        font-size: 12px;
        word-break: break-all;
        user-select: all;
      }
    }
  }

  .log-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--td-text-color-secondary);
    margin-bottom: 8px;
  }

  .log-scroll-area {
    background: var(--td-bg-color-secondarycontainer);
    border-radius: var(--td-radius-medium);
    padding: 12px;
    max-height: 200px;
    overflow-y: auto;
    border: 1px solid var(--td-component-border);

    &::-webkit-scrollbar {
      width: 6px;
    }
    &::-webkit-scrollbar-thumb {
      background: var(--td-scrollbar-color);
      border-radius: 4px;
    }

    .log-text {
      font-family: 'Menlo', 'Monaco', 'Courier New', monospace;
      font-size: 13px;
      line-height: 1.6;
      white-space: pre-wrap;
      color: var(--td-text-color-primary);
    }
  }
}

.modal-footer {
  .primary-actions {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .secondary-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }

  .sub-actions {
    margin-top: 16px;
    display: flex;
    justify-content: center;
  }
}

@media (max-width: 480px) {
  .secondary-grid {
    grid-template-columns: 1fr !important;
  }
}
</style>
