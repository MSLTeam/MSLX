<script lang="ts" setup>
import { ref, watch, computed, onUnmounted } from 'vue';
import { getResourceDetail, getResourceVersions, type ResourceVersionModel, type ResourceModel, type ResourceDependencyModel } from '@/api/resourceCenter';
import { MessagePlugin } from 'tdesign-vue-next';
import { addOfflineDownloadTask, getOfflineDownloadTaskStatus, getPluginsOrModsList } from '@/api/files';

const props = defineProps<{
  visible: boolean;
  version: ResourceVersionModel | null;
  mainResource: ResourceModel | null;
  instanceId?: number | null;
  resourceType?: number; // 0=Mod, 1=资源包, 2=数据包, 3=光影, 4=整合包, 5=插件
}>();

const emit = defineEmits(['update:visible', 'download-complete']);

const dialogVisible = computed({
  get: () => props.visible,
  set: (val) => emit('update:visible', val)
});

const loading = ref(false);
const resolvedDependencies = ref<any[]>([]);
const installedFiles = ref<string[]>([]);

const downloadMode = ref<'browser' | 'instance'>('browser');
const isSupportInstance = computed(() => props.resourceType === 0 || props.resourceType === 5);

watch([() => props.instanceId, () => props.resourceType], () => {
  if (props.instanceId && isSupportInstance.value) {
    downloadMode.value = 'instance';
  } else {
    downloadMode.value = 'browser';
  }
}, { immediate: true });

const resourceLabel = computed(() => {
  switch (props.resourceType) {
    case 0: return '主模组';
    case 1: return '材质包';
    case 2: return '数据包';
    case 3: return '光影包';
    case 4: return '整合包';
    case 5: return '主插件';
    default: return '目标资源';
  }
});

const loadDependencies = async () => {
  if (!props.version || !props.version.dependencies || props.version.dependencies.length === 0) {
    resolvedDependencies.value = [];
    return;
  }

  loading.value = true;
  resolvedDependencies.value = [];
  installedFiles.value = [];

  if (props.instanceId) {
    try {
      const res = await getPluginsOrModsList(props.instanceId, 'mods', false);
      if (res && res.jarFiles) {
        installedFiles.value = res.jarFiles.map(f => f.toLowerCase());
      }
    } catch { /* empty */ }
  }

  const visited = new Set<string>();
  const gameVersion = props.version.gameVersions?.[0] || '';
  const loader = props.version.loaders?.[0] || '';

  const resolveRecursive = async (deps: ResourceDependencyModel[]) => {
    const toProcess = deps.filter(d => (d.type === 0 || d.type === 1) && !visited.has(d.projectId));

    for (const dep of toProcess) {
      visited.add(dep.projectId);
      try {
        const detail = await getResourceDetail(dep.provider, dep.projectId);
        if (detail) {
          const versions = await getResourceVersions(dep.provider, dep.projectId, gameVersion, loader);
          let selected = null;
          if (versions && versions.length > 0) {
            selected = versions[0];
          }

          const slug = detail.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
          const isInstalled = selected && (
              installedFiles.value.includes(selected.filename.toLowerCase()) ||
              (slug.length > 2 && installedFiles.value.some(f => f.includes(slug)))
          );

          resolvedDependencies.value.push({
            ...dep,
            name: detail.name,
            iconUrl: detail.iconUrl,
            summary: detail.summary,
            selectedVersion: selected,
            candidateVersions: versions || [],
            alreadyInstalled: isInstalled
          });

          if (selected && selected.dependencies && selected.dependencies.length > 0) {
            await resolveRecursive(selected.dependencies);
          }
        }
      } catch (e) {
        console.error('Failed to load dependency', dep.projectId, e);
      }
    }
  };

  await resolveRecursive(props.version.dependencies);
  loading.value = false;
};

// 离线下载状态
interface DownloadTaskState {
  id: string;
  name: string;
  taskId?: string;
  progress: number;
  message: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
}
const downloadingState = ref(false);
const downloadTasks = ref<DownloadTaskState[]>([]);
let pollingTimer: ReturnType<typeof setInterval> | null = null;

const cleanupPolling = () => {
  if (pollingTimer) {
    clearInterval(pollingTimer);
    pollingTimer = null;
  }
};

onUnmounted(() => cleanupPolling());

watch(() => props.visible, (val) => {
  if (val) {
    downloadingState.value = false;
    downloadTasks.value = [];
    cleanupPolling();
    if (props.version) {
      loadDependencies();
    }
  }
});

const singleDownload = async (url: string, filename: string) => {
  if (downloadMode.value === 'instance' && props.instanceId && isSupportInstance.value) {
    try {
      const targetFolder = props.resourceType === 5 ? 'plugins' : 'mods';
      await addOfflineDownloadTask(props.instanceId, targetFolder, url, filename);
      MessagePlugin.success(`已向实例发送下载任务: ${filename}`);
    } catch (e) {
      MessagePlugin.error('下载任务发送失败 ' + e.message);
    }
  } else {
    window.open(url, '_blank');
  }
};

const handleDownloadAll = async () => {
  if (downloadMode.value === 'browser') {
    // 浏览器批量下载
    if (props.version) {
      window.open(props.version.downloadUrl, '_blank');
    }
    for (const dep of resolvedDependencies.value) {
      if (dep.type === 0 && dep.selectedVersion && !dep.alreadyInstalled) {
        window.open(dep.selectedVersion.downloadUrl, '_blank');
      }
    }
    emit('download-complete');
    dialogVisible.value = false;
    return;
  }

  // 服务端直装模式，进入进度展示
  if (!props.instanceId || !isSupportInstance.value) return;
  downloadingState.value = true;
  downloadTasks.value = [];

  const targetFolder = props.resourceType === 5 ? 'plugins' : 'mods';

  const addQueueItem = (name: string, url: string, filename: string) => {
    const state: DownloadTaskState = { id: url, name, progress: 0, message: '准备中...', status: 'pending' };
    downloadTasks.value.push(state);

    addOfflineDownloadTask(props.instanceId!, targetFolder, url, filename).then((res: any) => {
      const tid = res?.taskId;
      if (tid && typeof tid === 'string') {
        state.taskId = tid;
        state.message = '排队中...';
      } else {
        state.status = 'failed';
        state.message = '无法获取任务ID';
      }
    }).catch((e) => {
      state.status = 'failed';
      state.message = '发送任务失败 ' + e.message;
    });
  };

  if (props.version) {
    addQueueItem(props.mainResource?.name || resourceLabel.value, props.version.downloadUrl, props.version.filename);
  }
  for (const dep of resolvedDependencies.value) {
    if (dep.type === 0 && dep.selectedVersion && !dep.alreadyInstalled) {
      addQueueItem(dep.name, dep.selectedVersion.downloadUrl, dep.selectedVersion.filename);
    }
  }

  // 启动轮询
  cleanupPolling();
  pollingTimer = setInterval(async () => {
    let allDone = true;
    for (const task of downloadTasks.value) {
      if (task.status === 'completed' || task.status === 'failed') continue;
      allDone = false;
      if (task.taskId) {
        try {
          const res: any = await getOfflineDownloadTaskStatus(task.taskId);
          // 解析后端返回的 progress 和 message
          const statusData = res?.data || res;
          if (statusData) {
            task.progress = statusData.progress || 0;
            task.message = statusData.message || '';
            task.status = statusData.status || 'processing';

            // 后端返回状态为 error 等同于 failed
            if (task.status === 'error' as any) task.status = 'failed';
          }
        } catch { /* empty */ }
      }
    }
    if (allDone) {
      cleanupPolling();
    }
  }, 1000);
};
</script>

<template>
  <t-dialog v-model:visible="dialogVisible" :header="downloadingState ? '下载进度' : '下载确认与前置依赖'" width="600px" :footer="false">

    <!-- 进度视图 -->
    <div v-if="downloadingState" class="flex flex-col gap-3 pb-4">
      <div v-for="task in downloadTasks" :key="task.id" class="p-3 bg-[var(--td-bg-color-secondarycontainer)] rounded-lg">
        <div class="flex justify-between text-sm font-bold mb-2">
          <span>{{ task.name }}</span>
          <span :class="{
            'text-[var(--td-success-color)]': task.status === 'completed',
            'text-[var(--td-error-color)]': task.status === 'failed',
            'text-[var(--td-text-color-secondary)]': task.status === 'pending',
            'text-[var(--td-brand-color)]': task.status === 'processing'
          }">{{ task.status === 'completed' ? '已完成' : task.status === 'failed' ? '下载失败' : (task.progress + '%') }}</span>
        </div>
        <t-progress theme="plump" :percentage="task.progress" :color="task.status === 'failed' ? 'var(--td-error-color)' : undefined" />
        <div class="text-xs text-[var(--td-text-color-secondary)] mt-2">{{ task.message }}</div>
      </div>

      <div class="flex justify-end gap-2 mt-4 pt-4 border-t border-[var(--td-component-border)]">
        <t-button v-if="downloadTasks.every(t => t.status === 'completed' || t.status === 'failed')" theme="primary" @click="dialogVisible = false; emit('download-complete')">完成并关闭</t-button>
        <t-button v-else theme="default" @click="dialogVisible = false">后台下载</t-button>
      </div>
    </div>

    <!-- 确认视图 -->
    <div v-else class="flex flex-col gap-4 pb-4">
      <div v-if="loading" class="flex justify-center p-8">
        <t-loading text="正在解析前置依赖..." />
      </div>

      <template v-else>
        <!-- 下载模式选择 -->
        <div class="flex items-center justify-between p-3 bg-[var(--td-bg-color-container)] border border-[var(--td-component-border)] rounded-lg">
          <span class="font-bold text-[var(--td-text-color-primary)]">下载方式</span>
          <t-radio-group v-model="downloadMode" variant="default-filled">
            <t-radio-button value="browser">本地浏览器下载</t-radio-button>
            <t-radio-button value="instance" :disabled="!props.instanceId || !isSupportInstance">
              {{ isSupportInstance ? '直装到服务端实例' : '该类型不支持直装' }}
            </t-radio-button>
          </t-radio-group>
        </div>

        <!-- 主模组 -->
        <div class="p-3 bg-[var(--td-bg-color-container)] border border-[var(--td-component-border)] rounded-lg">
          <div class="font-bold text-[var(--td-text-color-primary)]">{{ resourceLabel }}: {{ mainResource?.name }}</div>
          <div class="text-sm text-[var(--td-text-color-secondary)]">{{ version?.name }}</div>
        </div>

        <!-- 前置依赖列表 -->
        <div v-if="resolvedDependencies.length > 0">
          <div class="font-bold mb-2">包含前置依赖:</div>
          <div class="flex flex-col gap-2 max-h-60 overflow-y-auto custom-scrollbar">
            <div v-for="dep in resolvedDependencies" :key="dep.projectId"
                 class="flex items-center justify-between p-2 border border-[var(--td-component-border)] rounded-lg bg-[var(--td-bg-color-secondarycontainer)]">
              <div class="flex items-center gap-3 min-w-0">
                <t-avatar :image="dep.iconUrl" size="32px" shape="round">
                  <template #icon>{{ dep.name?.charAt(0) || '?' }}</template>
                </t-avatar>
                <div class="min-w-0">
                  <div class="text-sm font-bold flex items-center gap-2">
                    {{ dep.name }}
                    <t-tag v-if="dep.type === 0" theme="danger" size="small" variant="light">必需</t-tag>
                    <t-tag v-else theme="primary" size="small" variant="light">可选</t-tag>
                    <t-tag v-if="dep.alreadyInstalled" theme="success" size="small" variant="light">已安装</t-tag>
                  </div>
                  <div class="text-xs text-[var(--td-text-color-secondary)] truncate">
                    {{ dep.selectedVersion?.name || '未找到兼容版本' }}
                  </div>
                </div>
              </div>
              <t-button v-if="dep.selectedVersion && !dep.alreadyInstalled" size="small" theme="default" @click="singleDownload(dep.selectedVersion.downloadUrl, dep.selectedVersion.filename)">单下</t-button>
            </div>
          </div>
        </div>

        <div class="flex justify-between items-center mt-4 pt-4 border-t border-[var(--td-component-border)]">
          <div class="text-sm text-[var(--td-text-color-secondary)]">
            <span v-if="downloadMode === 'instance' && !props.instanceId && isSupportInstance" class="text-[var(--td-error-color)]">请先在页面右上角选择目标实例</span>
          </div>
          <div class="flex gap-2">
            <t-button theme="default" @click="dialogVisible = false">取消</t-button>
            <t-button theme="primary" @click="handleDownloadAll">
              {{ downloadMode === 'browser' ? '下载' : '直装' }}
              ({{ (resolvedDependencies.filter(d => d.type === 0 && d.selectedVersion && !d.alreadyInstalled).length) + 1 }}个文件)
            </t-button>
          </div>
        </div>
      </template>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
.custom-scrollbar {
  &::-webkit-scrollbar {
    width: 6px;
  }
  &::-webkit-scrollbar-thumb {
    @apply bg-zinc-300 dark:bg-zinc-600 rounded-full;
  }
}
</style>
