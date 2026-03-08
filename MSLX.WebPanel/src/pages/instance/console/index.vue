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
const status = ref(0); // 0:未启动, 1:启动中, 2:运行中, 3:停止中, 4:重启中
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
    status.value = res.status;
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
    status.value = 0;
  }
};

// 停止
const handleStop = async () => {
  loading.value = true;
  status.value = 3;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送停止指令...\x1b[0m');
    await postInstanceAction(serverId.value, 'stop');
    // isRunning.value = false;
    MessagePlugin.warning('实例停止指令已发送');
    loading.value = false;
    instanceListStore.refreshInstanceList();
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 停止失败: ${e.message}\x1b[0m`);
    loading.value = false;
  }
};

// 强制退出
const handleForceExit = async () => {
  loading.value = true;
  status.value = 3;
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

// 重启
const handleRestart = async () => {
  loading.value = true;
  status.value = 4;
  try {
    terminalRef.value?.writeln('\x1b[1;32m[System] 正在发送重启指令...\x1b[0m');
    await postInstanceAction(serverId.value, 'restart');
    // isRunning.value = false;
    MessagePlugin.warning('重启执行成功');
    loading.value = false;
    status.value = 2;
  } catch (e: any) {
    terminalRef.value?.writeln(`\x1b[1;31m[Error] 重启失败: ${e.message}\x1b[0m`);
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

<<template>
  <div class="h-full flex flex-col md:flex-row gap-5 overflow-hidden pb-3 box-border relative text-zinc-800 dark:text-zinc-200">

    <div class="list-item-anim flex-1 min-w-0 h-[400px] md:h-full flex flex-col relative z-10" style="animation-delay: 0s;">
      <server-terminal ref="terminalRef" :server-id="serverId" @update="fetchServerInfo()" />
    </div>

    <div class="list-item-anim w-full md:w-80 lg:w-[340px] shrink-0 h-auto md:h-full overflow-y-auto custom-scrollbar pr-1 flex flex-col hide-scrollbar-on-mobile relative z-10" style="animation-delay: 0.1s;">
      <server-control-panel
        :server-id="serverId"
        :status="status"
        :loading="loading"
        :server-info="serverInfo"
        @start="handleStart"
        @stop="handleStop"
        @backup="handleBackup"
        @clear-log="handleClearLog"
        @force-exit="handleForceExit"
        @restart="handleRestart"
      />
    </div>

    <t-dialog
      v-model:visible="eulaDialogVisible"
      header="是否同意 EULA"
      :confirm-btn="{ content: '同意', theme: 'primary', class: '!rounded-lg !font-bold' }"
      :cancel-btn="{ content: '不同意', theme: 'default', class: '!rounded-lg !font-bold' }"
      @confirm="handleAgreeEula(true)"
      @cancel="handleAgreeEula(false)"
    >
      <div class="leading-relaxed text-sm">
        <p class="mb-3">
          开启 Minecraft 服务器需要您同意 <strong>EULA</strong> ！
          <t-link theme="primary" underline href="https://aka.ms/minecrafteula" target="_blank" class="font-mono">
            (https://aka.ms/minecrafteula)
          </t-link>
        </p>
        <p class="mb-3">
          <strong class="text-red-500 dark:text-red-400">请您务必认真仔细阅读！</strong>
        </p>
        <p class="mb-3 text-zinc-700 dark:text-zinc-300"><strong>注意：</strong>不论您选择是或否，服务器都会在您操作后继续运行。</p>
        <p class="mb-3 text-amber-500 dark:text-amber-400 font-medium">
          ⚠️ 如果您<strong>未同意 EULA</strong>，服务器可能会在运行时自动关闭！
        </p>
        <p class="mt-4 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700 text-zinc-500 dark:text-zinc-400 text-xs">
          💡 提示：如要在每次启动实例时忽略此提示，请在<strong>设置</strong>里进行配置。
        </p>
      </div>
    </t-dialog>
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

/* 移动端隐藏右侧面板的内滚动条，保持视觉干净 */
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
