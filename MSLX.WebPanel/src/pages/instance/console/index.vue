<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';

// 组件
import ServerTerminal from './components/ServerTerminal.vue';
import ServerControlPanel from './components/ServerControlPanel.vue';

const route = useRoute();

// 状态
const serverId = ref(parseInt(route.params.id as string) || 0);
const isRunning = ref(false);
const loading = ref(false);
const serverInfo = ref<any | null>(null); // 占位数据对象

// 引用终端组件实例
const terminalRef = ref<InstanceType<typeof ServerTerminal> | null>(null);

// 获取服务器详情
async function fetchServerInfo() {
  if (!serverId.value) return;
  try {
    loading.value = true;
    // TODO: 调用真实的 API
    // const res = await getServerInfo(serverId.value);
    // serverInfo.value = res;
    // isRunning.value = res.status === 'running';

    // 模拟数据
    setTimeout(() => {
      serverInfo.value = { id: serverId.value, name: 'Survival Server #1', port: 25565 };
      loading.value = false;
      //terminalRef.value?.writeln('\x1b[33m[System] 已获取最新的实例状态数据\x1b[0m');
    }, 500);

  } catch (error) {
    console.error('获取实例信息失败', error);
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 获取实例信息失败: ${error}\x1b[0m`);
    loading.value = false;
  }
}

// 启动
const handleStart = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送启动指令...\x1b[0m');
    // await startServer(serverId.value);

    // 模拟延迟
    setTimeout(() => {
      isRunning.value = true;
      MessagePlugin.success('实例启动指令已发送');
      loading.value = false;
    }, 1000);
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 启动失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

// 停止
const handleStop = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;33m[System] 正在发送停止指令...\x1b[0m');
    // await stopServer(serverId.value);

    // 模拟延迟
    setTimeout(() => {
      isRunning.value = false;
      MessagePlugin.warning('实例停止指令已发送');
      loading.value = false;
    }, 1000);
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 停止失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

const handleClearLog = () => {
  terminalRef.value?.clear();
};

// 监听路由参数变化
watch(
  () => route.params.id,
  async (newId) => {
    if (newId) {
      serverId.value = parseInt(newId as string);
      //terminalRef.value?.writeln(`\x1b[33m[System] 切换到实例 ID: ${serverId.value} \x1b[0m`);
      await fetchServerInfo();
    }
  },
);

onMounted(() => {
  if (serverId.value) {
    fetchServerInfo();
  }
});
</script>

<template>
  <div class="console-page">
    <div class="layout-container">

      <div class="main-terminal-area">
        <server-terminal
          ref="terminalRef"
          :server-id="serverId"
          @update="fetchServerInfo()"
        />
      </div>

      <div class="sidebar-area">
        <server-control-panel
          :server-id="serverId"
          :is-running="isRunning"
          :loading="loading"
          :server-info="serverInfo"
          @start="handleStart"
          @stop="handleStop"
          @clear-log="handleClearLog"
        />
      </div>

    </div>
  </div>
</template>

<style scoped lang="less">
.console-page {
  padding-bottom: 12px;
  background-color: var(--td-bg-color-page);
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
  .console-page { height: auto; overflow-y: auto; padding: 16px; }
  .layout-container { flex-direction: column; height: auto; }
  .sidebar-area { width: 100%; height: auto; }
  .main-terminal-area { min-height: 400px; margin-bottom: 16px; }
}
</style>
