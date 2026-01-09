<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';

// 组件
import ServerTerminal from './components/ServerTerminal.vue';
import ServerControlPanel from './components/ServerControlPanel.vue';
import { getInstanceInfo, postInstanceAction } from '@/api/instance';
import { InstanceInfoModel } from '@/api/model/instance';
import { useInstanceListStore } from '@/store/modules/instance';
import { useInstanceHubStore } from '@/store/modules/instanceHub';

const route = useRoute();

const instanceListStore = useInstanceListStore();

// 使用 Store
const hubStore = useInstanceHubStore();

// 状态
const serverId = ref(parseInt(route.params.serverId as string) || 0);
const isRunning = ref(false);
const loading = ref(false);
const serverInfo = ref<InstanceInfoModel>(null); // 占位数据对象

// 引用终端组件实例
const terminalRef = ref<InstanceType<typeof ServerTerminal> | null>(null);

// 获取服务器详情
async function fetchServerInfo() {
  if (!serverId.value) return;
  try {
    loading.value = true;
    const res = await getInstanceInfo(serverId.value);
    await instanceListStore.refreshInstanceList();
    isRunning.value = res.status;
    serverInfo.value = res;
    loading.value = false;
  } catch (error) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 获取实例信息失败: ${error.message}\x1b[0m`);
    loading.value = false;
  }
}

// 启动
const handleStart = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送启动指令...\x1b[0m');
    await postInstanceAction(serverId.value, 'start');
    // isRunning.value = true; // 由状态更新处理
    MessagePlugin.success('实例启动指令已发送');
    loading.value = false;
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 启动失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

// 停止
const handleStop = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送停止指令...\x1b[0m');
    await postInstanceAction(serverId.value, 'stop');
    // isRunning.value = false;
    MessagePlugin.warning('实例停止指令已发送');
    loading.value = false;
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 停止失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};


// 强制退出
const handleForceExit = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送强制退出指令...\x1b[0m');
    await postInstanceAction(serverId.value, 'forceExit');
    // isRunning.value = false;
    MessagePlugin.warning('强制退出指令已发送');
    loading.value = false;
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 强制退出失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

// 备份
const handleBackup = async () => {
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送备份任务...\x1b[0m');
    await postInstanceAction(serverId.value, 'backup');
    // isRunning.value = true; // 由状态更新处理
    MessagePlugin.success('备份任务启动中···');
    loading.value = false;
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 备份任务启动失败: ${e.message}\x1b[0m`);
  }
};

const handleClearLog = () => {
  terminalRef.value?.clear();
};
const eulaDialogVisible = ref(false);
// 连接 Store
const connectStore = async () => {
  if (!serverId.value) return;

  hubStore.onEula(() => {
    // 显示对话框
    eulaDialogVisible.value = true;
  });

  // 发起连接
  await hubStore.connect(serverId.value);
};

const handleAgreeEula = async (agreed) => {
  try {
    eulaDialogVisible.value = false;
    await postInstanceAction(serverId.value, `agreeEula?${agreed}`);
    MessagePlugin.success(agreed ? '已发送同意请求' : '已发送请求');
  } catch (e) {
    MessagePlugin.error(e.message || '发送失败');
  }
};

// 监听路由参数变化
watch(
  () => route.params.serverId,
  async (newId) => {
    if (route.name !== 'InstanceConsole') {
      return;
    }
    if (newId) {
      serverId.value = parseInt(newId as string);
      //terminalRef.value?.writeln(`\x1b[33m[System] 切换到实例 ID: ${serverId.value} \x1b[0m`);
      await fetchServerInfo();
    }
  },
);

onMounted(async () => {
  if (serverId.value) {
    await fetchServerInfo();
    await connectStore();
  }
});
</script>

<template>
  <div class="console-page">
    <div class="layout-container">
      <div class="main-terminal-area">
        <server-terminal ref="terminalRef" :server-id="serverId" @update="fetchServerInfo()" />
      </div>

      <div class="sidebar-area">
        <server-control-panel
          :server-id="serverId"
          :is-running="isRunning"
          :loading="loading"
          :server-info="serverInfo"
          @start="handleStart"
          @stop="handleStop"
          @backup="handleBackup"
          @clear-log="handleClearLog"
          @force-exit="handleForceExit"
        />
      </div>
    </div>
    <t-dialog
      v-model:visible="eulaDialogVisible"
      header="是否同意EULA"
      :confirm-btn="{
        content: '同意',
        theme: 'primary',
      }"
      :cancel-btn="{
        content: '不同意',
        theme: 'default',
      }"
      @confirm="handleAgreeEula(true)"
      @cancel="handleAgreeEula(false)"
    >
      <div style="line-height: 1.6">
        <p style="margin-bottom: 12px">
          开启Minecraft服务器需要您同意 <strong>EULA</strong> ！<t-link theme="primary" underline>
            (https://aka.ms/minecrafteula)</t-link
          >
        </p>
        <p style="margin-bottom: 12px">
          <strong style="color: #e34d59">请您务必认真仔细阅读！</strong>
        </p>
        <p style="margin-bottom: 12px"><strong>注意：</strong>不论您选择是或否，服务器都会在您操作后继续运行。</p>
        <p style="margin-bottom: 12px; color: #f59a23">
          ⚠️ 如果您<strong>未同意 EULA</strong>，服务器可能会在运行时自动关闭！
        </p>
        <p style="margin-top: 16px">💡 提示：如要在每次启动实例时忽略此提示，请在<strong>设置</strong>里进行配置。</p>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
.console-page {
  padding-bottom: 12px;
  height: calc(100vh - 64px);
  box-sizing: border-box;
  overflow: hidden;
}

.layout-container {
  display: flex;
  width: 100%;
  height: 100%;
  gap: 20px;
  align-items: stretch;
}

.main-terminal-area {
  flex: 1;
  min-width: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.sidebar-area {
  width: 320px;
  flex-shrink: 0;
  height: 100%;
  overflow-y: auto;
}

@media (max-width: 768px) {
  .console-page {
    height: auto;
    overflow-y: auto;
    padding: 16px;
  }
  .layout-container {
    flex-direction: column;
    height: auto;
  }
  .sidebar-area {
    width: 100%;
    height: auto;
  }
  .main-terminal-area {
    min-height: 400px;
    margin-bottom: 16px;
  }
}
</style>
