<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';

// 子组件
import ConsoleTerminal from './components/ConsoleTerminal.vue';
import ControlPanel from './components/ControlPanel.vue';

import { getTunnelInfo, postFrpAction } from '@/api/frp';
import { TunnelInfoModel } from '@/api/model/frp';
import { useTunnelsStore } from '@/store/modules/frp';

const tunnelsStore = useTunnelsStore();
const route = useRoute();

// 状态
const frpId = ref(parseInt(route.params.frpId as string) || 0);
const isRunning = ref(false);
const loading = ref(false);
const tunnelInfo = ref<TunnelInfoModel | null>(null);

// 引用终端组件实例
const terminalRef = ref<InstanceType<typeof ConsoleTerminal> | null>(null);

// 获取隧道详情
async function fetchTunnelInfo() {
  if (!frpId.value) return;
  try {
    tunnelInfo.value = await getTunnelInfo(frpId.value);
    isRunning.value = tunnelInfo.value.isRunning;
    // 刷新全局列表状态
    await tunnelsStore.getTunnels();
  } catch (error) {
    console.error('获取隧道信息失败', error);
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 获取隧道信息失败: ${error}\x1b[0m`);
  }
}

// 业务操作
const handleStart = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送启动指令...\x1b[0m');
    await postFrpAction('start', frpId.value);
    isRunning.value = true;
    MessagePlugin.success('启动指令已发送');
    // 延迟刷新以等待服务端状态变更
    setTimeout(fetchTunnelInfo, 1500);
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] Frpc启动失败: ${e.message}\x1b[0m`);
  } finally {
    loading.value = false;
  }
};

const handleStop = async () => {
  loading.value = true;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送停止指令...\x1b[0m');
    await postFrpAction('stop', frpId.value);
    isRunning.value = false;
    MessagePlugin.warning('停止指令已发送');
    setTimeout(fetchTunnelInfo, 1000);
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] Frpc停止失败: ${e.message}\x1b[0m`);
  } finally {
    loading.value = false;
  }
};

const handleClearLog = () => {
  terminalRef.value?.clear();
};

// 监听路由参数变化
watch(
  () => route.params.frpId,
  async (newId) => {
    if (route.name !== 'FrpConsole') {
      return;
    }
    if (newId) {
      frpId.value = parseInt(newId as string);
      // terminalRef.value?.writeln('\x1b[33m[System] 检测到 Frp ID 变更，正在刷新数据...\x1b[0m');
      await fetchTunnelInfo();
    }
  },
);

onMounted(() => {
  if (frpId.value) {
    fetchTunnelInfo();
  }
});
</script>

<template>
  <div class="console-page">
    <div class="layout-container">

      <div class="main-terminal-area">
        <console-terminal
          ref="terminalRef"
          :frp-id="frpId"
          @update="fetchTunnelInfo()"
        />
      </div>

      <div class="sidebar-area">
        <control-panel
          :frp-id="frpId"
          :is-running="isRunning"
          :loading="loading"
          :tunnel-info="tunnelInfo"
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
