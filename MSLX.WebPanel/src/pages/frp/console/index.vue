<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';

// 子组件
import ConsoleTerminal from './components/ConsoleTerminal.vue';
import ControlPanel from './components/ControlPanel.vue';
import FileEditor from '@/pages/instance/files/components/FileEditor.vue';

import { getTunnelInfo, postFrpAction } from '@/api/frp';
import { TunnelInfoModel } from '@/api/model/frp';
import { useTunnelsStore } from '@/store/modules/frp';
import { getFileContent, saveFileContent } from '@/api/files';
import PluginSlot from '@/components/PluginSlot.vue';

const tunnelsStore = useTunnelsStore();
const route = useRoute();

// 状态
const frpId = ref(parseInt(route.params.frpId as string) || 0);
const isRunning = ref(false);
const loading = ref(false);
const tunnelInfo = ref<TunnelInfoModel | null>(null);

// 编辑器状态
const showEditor = ref(false);
const editorFileName = ref('');
const editorContent = ref('');
const isSaving = ref(false);

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
    terminalRef.value?.writeln(
      '\x1b[1;35m\x1b[1m[TIPS] 注意：日志可能包含您的在线服务商的Token信息，若需要截图，请将关键信息打码处理！\x1b[0m',
    );
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

const handleEditConfig = () => {
  const confirmDialog = DialogPlugin.confirm({
    header: '警告',
    body: '直接编辑配置文件可能会导致服务无法启动或异常。正常情况在线服务商提供的配置文件也不能修改。请确保您了解配置文件的格式和内容。\n\n是否继续？',
    theme: 'warning',
    onConfirm: async () => {
      confirmDialog.hide();
      await openConfigFile();
    },
  });
};

const openConfigFile = async () => {
  const msg = MessagePlugin.loading('正在读取配置文件...');
  try {
    let ext = 'toml';
    const currentFrp = tunnelsStore.frpList.find((item) => item.id === frpId.value);

    if (currentFrp && currentFrp.configType) {
      ext = currentFrp.configType.toLowerCase();
    }
    const fileName = `frpc.${ext}`;
    const fullPath = `${frpId.value}/${fileName}`;

    // Instance ID 固定为 0
    const content = await getFileContent(0, fullPath);

    editorFileName.value = fileName;
    editorContent.value = content;
    showEditor.value = true;
    MessagePlugin.close(msg);
  } catch (err: any) {
    MessagePlugin.close(msg);
    MessagePlugin.error('读取配置文件失败: ' + err.message);
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 读取配置文件失败: ${err.message}\x1b[0m`);
  }
};

const handleSaveConfig = async (newContent: string) => {
  isSaving.value = true;
  try {
    const fullPath = `${frpId.value}/${editorFileName.value}`;
    // Instance ID 固定为 0
    await saveFileContent(0, fullPath, newContent);

    MessagePlugin.success('配置文件保存成功');
    showEditor.value = false;

    terminalRef.value?.writeln('\x1b[1;32m[System] 配置文件已更新，请重启服务以生效。\x1b[0m');
  } catch (err: any) {
    MessagePlugin.error('保存失败');
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 保存失败: ${err.message}\x1b[0m`);
  } finally {
    isSaving.value = false;
  }
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
  <div
    class="h-auto md:h-full flex flex-col md:flex-row gap-5 overflow-y-auto md:overflow-hidden pb-3 box-border relative"
  >
    <div
      class="list-item-anim w-full min-h-[400px] shrink-0 md:flex-1 md:min-h-0 md:h-full flex flex-col"
      style="animation-delay: 0s"
    >
      <console-terminal ref="terminalRef" :frp-id="frpId" @update="fetchTunnelInfo()" />
    </div>

    <div
      class="list-item-anim w-full md:w-80 lg:w-[340px] shrink-0 h-auto md:h-full overflow-y-auto custom-scrollbar md:pr-1 flex flex-col hide-scrollbar-on-mobile"
      style="animation-delay: 0.1s"
    >
      <control-panel
        :frp-id="frpId"
        :is-running="isRunning"
        :loading="loading"
        :tunnel-info="tunnelInfo"
        @start="handleStart"
        @stop="handleStop"
        @clear-log="handleClearLog"
        @edit-config="handleEditConfig"
      />
      <!--插件扩展区域 frp-console-control-panel-bottom -->
      <plugin-slot
        name="frp-console-control-panel-bottom"
        :frp-id="frpId"
        :is-running="isRunning"
      />
    </div>

    <file-editor
      v-model:visible="showEditor"
      :file-name="editorFileName"
      :content="editorContent"
      :loading="isSaving"
      @save="handleSaveConfig"
    />
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.6s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from {
    transform: translateY(20px);
  }
  to {
    transform: translateY(0);
  }
}

.list-item-anim {
  :deep(.terminal-wrapper) {
    animation: glassFadeIn 0.6s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
    animation-delay: inherit;
  }
}

@keyframes glassFadeIn {
  from {
    opacity: 0;
    backdrop-filter: blur(0px) !important;
    -webkit-backdrop-filter: blur(0px) !important;
  }
}

.custom-scrollbar {
  .scrollbar-mixin();
}

/* 移动端隐藏右侧面板的内滚动条 */
.hide-scrollbar-on-mobile::-webkit-scrollbar {
  @media (max-width: 768px) {
    display: none;
  }
}
.hide-scrollbar-on-mobile {
  @media (max-width: 768px) {
    scrollbar-width: none;
    -ms-overflow-style: none;
  }
}
</style>
