<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon } from 'tdesign-icons-vue-next';
import { useUserStore } from '@/store';
import {
  addOfflineDownloadTask,
  getOfflineDownloadTaskStatus,
  startDecompress,
  getDeompressStatus,
  changeFileMode,
} from '@/api/files';
import { getServerCoreDownloadInfo, getServerCoreGameVersion } from '@/api/mslapi/serverCore';

const props = defineProps<{
  visible: boolean;
  instanceId: number;
}>();

const emit = defineEmits(['update:visible', 'success']);

// 状态管理
const userStore = useUserStore();
const osType = computed(() => userStore.userInfo.systemInfo.osType?.toLowerCase() || '');
const isWin = computed(() => osType.value.includes('window'));

const isVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val),
});

const versions = ref<{ label: string; value: string }[]>([]);
const selectedVersion = ref('');
const isFetchingVersions = ref(false);

const isProcessing = ref(false);
const currentAction = ref<'idle' | 'fetching' | 'downloading' | 'unzipping' | 'chmodding' | 'success' | 'error'>(
  'idle',
);
const progress = ref(0);
const statusMsg = ref('');

// 初始化
const fetchVersions = async () => {
  isFetchingVersions.value = true;
  try {
    const res = await getServerCoreGameVersion('bedrock-server');
    const allVersions = res.versions || [];

    // 结果过滤
    let filteredVersions = [];
    if (isWin.value) {
      filteredVersions = allVersions.filter((v) => v.includes('win-'));
    } else {
      filteredVersions = allVersions.filter((v) => v.includes('linux-'));
    }

    versions.value = filteredVersions.map((v) => ({ label: v, value: v }));
    if (versions.value.length > 0) {
      selectedVersion.value = versions.value[0].value;
    }
  } catch (e: any) {
    MessagePlugin.warning('获取版本列表失败: ' + e.message);
  } finally {
    isFetchingVersions.value = false;
  }
};

watch(
  () => props.visible,
  (val) => {
    if (val) {
      isProcessing.value = false;
      currentAction.value = 'idle';
      progress.value = 0;
      statusMsg.value = '';
      if (versions.value.length === 0) {
        fetchVersions();
      }
    }
  },
);

// 轮询下载进度
const pollDownload = (taskId: string) => {
  return new Promise((resolve, reject) => {
    const timer = setInterval(async () => {
      try {
        const statusData = await getOfflineDownloadTaskStatus(taskId);
        progress.value = statusData.progress;
        statusMsg.value = statusData.message;

        if (statusData.status === 'success') {
          clearInterval(timer);
          resolve(true);
        } else if (statusData.status === 'error') {
          clearInterval(timer);
          reject(new Error(statusData.message || '下载失败'));
        }
      } catch (err) {
        clearInterval(timer);
        reject(err);
      }
    }, 1000);
  });
};

// 轮询解压进度
const pollDecompress = (taskId: string) => {
  return new Promise((resolve, reject) => {
    const timer = setInterval(async () => {
      try {
        const statusData: any = await getDeompressStatus(taskId);
        progress.value = statusData.progress || 0;
        statusMsg.value = statusData.message;

        if (statusData.status === 'success') {
          clearInterval(timer);
          resolve(true);
        } else if (statusData.status === 'error') {
          clearInterval(timer);
          reject(new Error(statusData.message || '解压出错'));
        }
      } catch (err) {
        clearInterval(timer);
        reject(err);
      }
    }, 1000);
  });
};

// 自动更新逻辑
const handleStartUpdate = () => {
  if (!selectedVersion.value) {
    MessagePlugin.warning('请选择要更新的版本');
    return;
  }

  const confirmDialog = DialogPlugin.confirm({
    header: '高危操作确认',
    theme: 'warning',
    body: '即将开始自动下载并覆盖更新基岩版服务端。请确认您已备份核心数据（如 worlds 目录）。此更新为前台任务，请勿在更新期间关闭或刷新此页面，否则可能导致服务端文件损坏！',
    confirmBtn: '我已备份，开始更新',
    cancelBtn: '取消',
    onConfirm: () => {
      confirmDialog.hide();
      executeAutoUpdate();
    },
  });
};

const executeAutoUpdate = async () => {
  isProcessing.value = true;
  currentAction.value = 'fetching';
  progress.value = 0;
  statusMsg.value = '正在解析下载地址...';

  try {
    // 获取下载地址
    const dlInfo = await getServerCoreDownloadInfo('bedrock-server', selectedVersion.value);
    if (!dlInfo || !dlInfo.url) throw new Error('无法获取版本下载链接');
    const targetFileName = 'bedrock_update_temp.zip';

    // 下载
    currentAction.value = 'downloading';
    statusMsg.value = '正在提交离线下载任务...';
    const dlRes: any = await addOfflineDownloadTask(props.instanceId, '', dlInfo.url, targetFileName);
    const dlTaskId = dlRes.taskId || (dlRes.data && dlRes.data.taskId);
    if (!dlTaskId) throw new Error('未能获取下载任务ID');

    await pollDownload(dlTaskId);

    // 解压
    currentAction.value = 'unzipping';
    progress.value = 0;
    statusMsg.value = '正在解压并覆盖服务端文件...';
    const unRes: any = await startDecompress(props.instanceId, targetFileName, '', 'auto', false);
    const unTaskId = unRes.taskId || (unRes.data && unRes.data.taskId);
    if (!unTaskId) throw new Error('未能获取解压任务ID');

    await pollDecompress(unTaskId);

    // 赋权
    if (!isWin.value) {
      currentAction.value = 'chmodding';
      statusMsg.value = '正在赋予可执行权限...';
      await changeFileMode(props.instanceId, 'bedrock_server', '755');
    }

    // end～
    currentAction.value = 'success';
    progress.value = 100;
    statusMsg.value = '基岩版服务端更新成功！';

    setTimeout(() => {
      emit('success');
      isVisible.value = false;
    }, 2000);
  } catch (err: any) {
    currentAction.value = 'error';
    statusMsg.value = err.message || '更新过程中发生异常';
    MessagePlugin.error(statusMsg.value);
  }
};

const handleClose = () => {
  if (isProcessing.value && currentAction.value !== 'success' && currentAction.value !== 'error') {
    MessagePlugin.warning('更新任务正在进行中，请勿关闭窗口');
    return;
  }
  isVisible.value = false;
};
</script>

<template>
  <t-dialog
    v-model:visible="isVisible"
    header="自动更新基岩版"
    :footer="false"
    :close-on-overlay-click="!isProcessing"
    :on-close="handleClose"
    width="480px"
    attach="body"
  >
    <div v-if="currentAction === 'idle'" class="flex flex-col gap-4 py-2">
      <div
        class="bg-blue-50 dark:bg-blue-900/20 p-3 rounded-xl border border-blue-200/60 dark:border-blue-800/60 text-[13px] text-blue-800 dark:text-blue-300 shadow-inner"
      >
        自动更新程序将下载官方服务端并解压覆盖到实例根目录。配置和地图数据通常不会丢失，但仍<strong>强烈建议</strong>事先备份。
      </div>

      <div class="flex flex-col gap-2 mt-2">
        <span class="text-sm font-medium text-[var(--td-text-color-primary)]">目标版本</span>
        <t-select
          v-model="selectedVersion"
          :options="versions"
          :loading="isFetchingVersions"
          filterable
          class="!rounded-lg shadow-sm"
          placeholder="请选择要更新的版本"
        />
      </div>

      <div class="flex justify-end gap-3 mt-4">
        <t-button variant="outline" class="!rounded-lg hover:!bg-zinc-100 dark:hover:!bg-zinc-800" @click="handleClose"
          >取消</t-button
        >
        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleStartUpdate" :disabled="!selectedVersion"
          >开始更新</t-button
        >
      </div>
    </div>

    <div v-else class="flex flex-col items-center gap-4 py-4 w-full">
      <div class="flex justify-center items-center h-10">
        <check-circle-filled-icon v-if="currentAction === 'success'" class="text-emerald-500 text-[40px]" />
        <error-circle-filled-icon v-else-if="currentAction === 'error'" class="text-red-500 text-[40px]" />
        <t-loading v-else size="medium" />
      </div>

      <div class="text-sm font-medium text-[var(--td-text-color-primary)] text-center px-4 w-full truncate">
        {{ statusMsg }}
      </div>

      <div class="w-full">
        <t-progress
          theme="plump"
          :percentage="progress"
          :status="currentAction === 'error' ? 'error' : currentAction === 'success' ? 'success' : 'active'"
        />
      </div>

      <div v-if="currentAction === 'error'" class="mt-4">
        <t-button theme="primary" variant="outline" @click="currentAction = 'idle'">返回重试</t-button>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
