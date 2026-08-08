<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';
import {
  CloudDownloadIcon,
  DeleteIcon,
  InfoCircleIcon,
  CheckCircleIcon,
  LayersIcon,
  LinkIcon,
  RefreshIcon,
  BrushIcon,
} from 'tdesign-icons-vue-next';
import {
  getDockerImages,
  getDockerPresetImages,
  getDockerStatus,
  postDeleteDockerImage,
  postPruneDockerImages,
  postCheckDockerImageUpdate,
} from '@/api/docker';
import {
  DockerEnvStatusModel,
  DockerImageModel,
  DockerPresetImageModel,
  DockerImageCheckUpdateItemModel,
} from '@/api/model/docker';
import NodeSwitcher from '@/components/node-switcher/index.vue';
import { copyText } from '@/utils/clipboard';
import PullImageDialog from './components/PullImageDialog.vue';
import ImageInspectDialog from './components/ImageInspectDialog.vue';

const loading = ref(false);
const statusLoading = ref(false);
const status = ref<DockerEnvStatusModel | null>(null);
const images = ref<DockerImageModel[]>([]);
const presets = ref<DockerPresetImageModel[]>([]);
const keyword = ref('');
const showDangling = ref(true);

const pullVisible = ref(false);
const pullPreset = ref('');
const inspectVisible = ref(false);
const inspectReference = ref('');

const updateStatusMap = ref<Record<string, DockerImageCheckUpdateItemModel>>({});
const isCheckingUpdate = ref(false);

const dockerReady = computed(() => status.value?.available === true);

// 预设提示内容
const unavailableTip = computed(() => {
  if (!status.value || status.value.available) return null;

  switch (status.value.errorType) {
    case 'notInstalled':
      return {
        title: '未检测到 docker 命令',
        desc: '请先在当前节点所在的宿主机安装 Docker，安装完成后点击右上角重新检测。',
      };
    case 'sockNotMounted':
      return {
        title: '容器内未挂载 Docker 通信管道',
        desc: 'MSLX 运行在容器中，需要挂载 /var/run/docker.sock:/var/run/docker.sock 后重启容器。详见文档：https://mslx.mslmc.cn/docs/install/docker/',
      };
    case 'permissionDenied':
      return {
        title: '无权访问 Docker 守护进程',
        desc: '请将 MSLX 的运行用户加入 docker 用户组，或以具备权限的身份运行守护进程。',
      };
    case 'daemonUnreachable':
      return {
        title: '无法连接 Docker 守护进程',
        desc: 'docker 命令存在但守护进程未响应，请确认 Docker 服务已启动。',
      };
    default:
      return {
        title: 'Docker 环境不可用',
        desc: status.value.errorMessage || '未知原因，请检查宿主机 Docker 状态。',
      };
  }
});

const filteredImages = computed(() => {
  const kw = keyword.value.trim().toLowerCase();
  return images.value
    .filter((item) => showDangling.value || !item.isDangling)
    .filter((item) => {
      if (!kw) return true;
      return (
        item.reference.toLowerCase().includes(kw) ||
        item.repository.toLowerCase().includes(kw) ||
        item.tag.toLowerCase().includes(kw) ||
        item.shortId.toLowerCase().includes(kw)
      );
    });
});

const totalSizeText = computed(() => {
  const total = images.value.reduce((sum, item) => sum + (item.sizeBytes || 0), 0);
  if (!total) return '-';

  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = total;
  let idx = 0;
  while (value >= 1024 && idx < units.length - 1) {
    value /= 1024;
    idx += 1;
  }
  return `${value.toFixed(idx === 0 ? 0 : 2)} ${units[idx]}`;
});

const danglingCount = computed(() => images.value.filter((i) => i.isDangling).length);

const columns = [
  { colKey: 'reference', title: '镜像', ellipsis: true, minWidth: 260 },
  { colKey: 'status', title: '版本状态', width: 120 },
  { colKey: 'shortId', title: '镜像 ID', width: 130 },
  { colKey: 'size', title: '大小', width: 110 },
  { colKey: 'createdAt', title: '创建时间', width: 190, ellipsis: true },
  { colKey: 'usedBy', title: '引用实例', width: 160 },
  { colKey: 'op', title: '操作', width: 220, fixed: 'right' },
];

const fetchStatus = async () => {
  statusLoading.value = true;
  try {
    status.value = await getDockerStatus(true);
  } catch (error: any) {
    status.value = null;
    MessagePlugin.error(`Docker 环境检测失败：${error?.message ?? error}`);
  } finally {
    statusLoading.value = false;
  }
};

const fetchImages = async () => {
  if (!dockerReady.value) {
    images.value = [];
    presets.value = [];
    return;
  }

  loading.value = true;
  try {
    const [imageList, presetList] = await Promise.all([getDockerImages(true), getDockerPresetImages()]);
    images.value = imageList || [];
    presets.value = presetList || [];
  } catch (error: any) {
    MessagePlugin.error(`获取镜像列表失败：${error?.message ?? error}`);
  } finally {
    loading.value = false;
  }
};

const refreshAll = async () => {
  await fetchStatus();
  await fetchImages();
};

const handleCheckUpdates = async (references?: string[], silent = false) => {
  if (!dockerReady.value) return;
  isCheckingUpdate.value = true;
  try {
    const list = await postCheckDockerImageUpdate(references);
    if (list && Array.isArray(list)) {
      list.forEach((item) => {
        updateStatusMap.value[item.reference] = item;
      });
      const hasUpdateCount = list.filter((i) => i.hasUpdate).length;
      if (!silent) {
        if (hasUpdateCount > 0) {
          MessagePlugin.warning(`检测完成，发现 ${hasUpdateCount} 个镜像有新版本可更新！`);
        } else {
          MessagePlugin.success('检测完成，镜像均为最新版本');
        }
      } else if (hasUpdateCount > 0) {
        MessagePlugin.warning(`发现 ${hasUpdateCount} 个镜像有新版本，可在列表中进行更新！`);
      }
    }
  } catch (error: any) {
    if (!silent) MessagePlugin.error(`检查更新失败：${error?.message ?? error}`);
  } finally {
    isCheckingUpdate.value = false;
  }
};

const handleNodeChange = () => {
  images.value = [];
  presets.value = [];
  refreshAll();
};

const openPullDialog = (preset = '') => {
  pullPreset.value = preset;
  pullVisible.value = true;
};

const handlePullFinished = async () => {
  await fetchImages();
  if (pullPreset.value) {
    updateStatusMap.value[pullPreset.value] = {
      reference: pullPreset.value,
      hasUpdate: false,
      status: 'upToDate',
    };
  }
  handleCheckUpdates(pullPreset.value ? [pullPreset.value] : undefined, true);
};

const openInspect = (reference: string) => {
  inspectReference.value = reference;
  inspectVisible.value = true;
};

const doDelete = async (reference: string, force: boolean) => {
  const res = await postDeleteDockerImage({ reference, force });
  MessagePlugin.success(res?.message || '镜像已删除');
  await fetchImages();
};

// 被占用删除的二次确认
const confirmForceDelete = (reference: string, reason: string) => {
  const dialog = DialogPlugin.confirm({
    header: '镜像正在被占用',
    body: `${reason}\n\n强制删除会解除标签并可能影响已有实例/容器，是否继续？`,
    theme: 'danger',
    confirmBtn: { content: '强制删除', theme: 'danger' },
    onConfirm: async () => {
      dialog.hide();
      try {
        await doDelete(reference, true);
      } catch (error: any) {
        MessagePlugin.error(`强制删除失败：${error?.message ?? error}`);
      }
    },
  });
};

const handleDelete = (row: DockerImageModel) => {
  const usedByText = row.usedBy?.length
    ? `\n\n注意：该镜像正被 ${row.usedBy.map((u) => `#${u.instanceId} ${u.instanceName}`).join('、')} 引用。`
    : '';

  const dialog = DialogPlugin.confirm({
    header: '确认删除镜像',
    body: `确定要删除 ${row.reference} 吗？${usedByText}`,
    theme: 'danger',
    onConfirm: async () => {
      dialog.hide();
      try {
        await doDelete(row.reference, false);
      } catch (error: any) {
        const message: string = error?.message ?? String(error);
        // 后端 409：镜像被实例引用或存在关联容器
        if (message.includes('强制删除')) {
          confirmForceDelete(row.reference, message);
          return;
        }
        MessagePlugin.error(`删除失败：${message}`);
      }
    },
  });
};

const handlePrune = () => {
  const dialog = DialogPlugin.confirm({
    header: '清理无用镜像',
    body: `将执行 docker image prune，删除所有无标签（dangling）镜像，当前共 ${danglingCount.value} 个。此操作不可恢复，是否继续？`,
    theme: 'warning',
    onConfirm: async () => {
      dialog.hide();
      const msg = MessagePlugin.loading('正在清理...');
      try {
        const res = await postPruneDockerImages();
        MessagePlugin.success(res?.message || '清理完成');
        await fetchImages();
      } catch (error: any) {
        MessagePlugin.error(`清理失败：${error?.message ?? error}`);
      } finally {
        MessagePlugin.close(msg);
      }
    },
  });
};

onMounted(async () => {
  await refreshAll();
  if (dockerReady.value) {
    handleCheckUpdates(undefined, true);
  }
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <!-- 标题栏 -->
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight m-0">Docker 本地镜像</h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">
          管理当前节点宿主机上的本地镜像，供 Docker 模式实例使用
        </p>
      </div>

      <div class="flex items-center gap-3">
        <node-switcher @change="handleNodeChange" />
        <t-button variant="dashed" :loading="statusLoading || loading" @click="refreshAll">
          <template #icon><refresh-icon /></template>
          重新检测
        </t-button>
      </div>
    </div>

    <!-- 环境状态 -->
    <div
      class="design-card flex flex-col gap-3 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm"
    >
      <div
        v-if="statusLoading && !status"
        class="flex items-center gap-2 text-sm text-[var(--td-text-color-secondary)]"
      >
        <t-loading size="small" />
        正在检测 Docker 环境...
      </div>

      <template v-else-if="dockerReady">
        <div class="flex flex-wrap items-center gap-3">
          <t-tag theme="success" variant="light" shape="round" class="!px-3 !font-medium">Docker 可用</t-tag>
          <span class="text-xs text-[var(--td-text-color-secondary)] font-mono">
            客户端 {{ status?.clientVersion || '-' }} / 服务端 {{ status?.serverVersion || '-' }}
          </span>
          <span class="text-xs text-[var(--td-text-color-secondary)] font-mono"
            >系统类型 {{ status?.osType || '-' }}</span
          >
          <t-tag v-if="status?.inContainer" theme="primary" variant="light" size="small">MSLX 运行于容器内</t-tag>
        </div>
      </template>

      <template v-else>
        <div class="flex flex-col gap-2">
          <div class="flex items-center gap-2">
            <info-circle-icon class="text-amber-500" />
            <span class="text-sm font-bold">{{ unavailableTip?.title }}</span>
          </div>
          <p class="text-xs text-[var(--td-text-color-secondary)] m-0 leading-relaxed break-all">
            {{ unavailableTip?.desc }}
          </p>
          <p
            v-if="status?.errorMessage"
            class="text-[11px] font-mono text-[var(--td-text-color-placeholder)] m-0 break-all"
          >
            {{ status.errorMessage }}
          </p>
        </div>
      </template>
    </div>

    <!-- 内置运行时镜像 -->
    <div
      v-if="dockerReady && presets.length"
      class="design-card flex flex-col gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm"
    >
      <div class="flex flex-col gap-1">
        <h3 class="text-base font-bold m-0 flex items-center gap-2">
          <layers-icon class="text-[var(--td-text-color-secondary)]" />
          MSLX 内置运行时
        </h3>
        <p class="text-xs text-[var(--td-text-color-secondary)] m-0">
          实例选择「MSLX Docker 镜像」时使用的运行时。
        </p>
      </div>

      <div class="flex flex-wrap gap-3">
        <div
          v-for="preset in presets"
          :key="preset.pseudo"
          class="flex items-center gap-3 px-3 py-2 rounded-xl border border-[var(--td-component-border)] bg-zinc-50/80 dark:bg-zinc-800/40"
        >
          <span class="w-2 h-2 rounded-full shrink-0" :class="preset.exists ? 'bg-emerald-500' : 'bg-zinc-400'"></span>
          <div class="flex flex-col">
            <span class="text-sm font-medium">{{ preset.label }}</span>
            <span class="text-[11px] font-mono text-[var(--td-text-color-placeholder)]">
              {{ preset.exists ? `已拉取 · ${preset.size}` : '本地不存在' }}
            </span>
          </div>
          <t-button
            v-if="!preset.exists"
            size="small"
            variant="text"
            theme="primary"
            @click="openPullDialog(preset.image)"
          >
            <template #icon><cloud-download-icon /></template>
            拉取
          </t-button>
          <t-button
            v-else-if="updateStatusMap[preset.image]?.hasUpdate"
            size="small"
            variant="text"
            theme="warning"
            @click="openPullDialog(preset.image)"
          >
            <template #icon><cloud-download-icon /></template>
            更新
          </t-button>
        </div>
      </div>
    </div>

    <!-- 镜像列表 -->
    <div
      v-if="dockerReady"
      class="design-card flex flex-col gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm"
    >
      <div class="flex flex-col lg:flex-row lg:items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h3 class="text-base font-bold m-0">本地镜像（{{ filteredImages.length }}）</h3>
          <t-tag variant="light" size="small">占用 {{ totalSizeText }}</t-tag>
          <t-tag v-if="danglingCount" theme="warning" variant="light" size="small">
            {{ danglingCount }} 个无标签镜像
          </t-tag>
        </div>

        <div class="flex flex-wrap items-center gap-3">
          <t-input v-model="keyword" placeholder="搜索仓库 / 标签 / ID" clearable class="!w-56" />
          <t-checkbox v-model="showDangling">显示无标签镜像</t-checkbox>
          <t-button variant="outline" theme="primary" :loading="isCheckingUpdate" @click="() => handleCheckUpdates()">
            <template #icon><refresh-icon /></template>
            检查更新
          </t-button>
          <t-button theme="primary" @click="openPullDialog('')">
            <template #icon><cloud-download-icon /></template>
            拉取镜像
          </t-button>
          <t-button variant="outline" theme="warning" :disabled="!danglingCount" @click="handlePrune">
            <template #icon><brush-icon /></template>
            清理无用镜像
          </t-button>
          <t-button variant="dashed" :loading="loading" @click="fetchImages">
            <template #icon><refresh-icon /></template>
            刷新
          </t-button>
        </div>
      </div>

      <t-table
        row-key="imageId"
        :data="filteredImages"
        :columns="columns as any"
        :loading="loading"
        size="small"
        :pagination="filteredImages.length > 10 ? { pageSize: 10 } : null"
      >
        <template #reference="{ row }">
          <div class="flex items-center gap-2 min-w-0">
            <span v-if="row.isDangling" class="text-xs text-[var(--td-text-color-placeholder)] font-mono truncate">
              &lt;none&gt;:&lt;none&gt;
            </span>
            <span v-else class="text-sm font-medium font-mono truncate" :title="row.reference">{{
              row.reference
            }}</span>
            <t-tag v-if="row.isMslxRuntime" theme="primary" variant="light" size="small" class="shrink-0"
              >内置运行时</t-tag
            >
            <t-tag
              v-if="row.reference === 'docker.mslmc.cn/xiaoyululu/mslx-runtime:network-tool'"
              theme="success"
              variant="light"
              size="small"
              class="shrink-0"
              >网络工具 (勿删)</t-tag
            >
            <t-tag v-if="row.isDangling" theme="warning" variant="light" size="small" class="shrink-0">无标签</t-tag>
          </div>
        </template>

        <template #status="{ row }">
          <div class="flex items-center">
            <template v-if="!row.isDangling && updateStatusMap[row.reference]">
              <t-tag
                v-if="updateStatusMap[row.reference].hasUpdate"
                theme="warning"
                variant="light"
                size="small"
                class="!font-medium cursor-pointer inline-flex items-center shrink-0"
                @click="openPullDialog(row.reference)"
              >
                <template #icon><cloud-download-icon /></template>
                可更新
              </t-tag>
              <t-tag
                v-else-if="updateStatusMap[row.reference].status === 'upToDate'"
                theme="success"
                variant="light"
                size="small"
                class="!font-medium inline-flex items-center shrink-0"
              >
                <template #icon><check-circle-icon /></template>
                已是最新
              </t-tag>
              <t-tag
                v-else
                theme="danger"
                variant="light"
                size="small"
                class="!font-medium inline-flex items-center shrink-0"
                :title="updateStatusMap[row.reference].message"
              >
                <template #icon><info-circle-icon /></template>
                检测失败
              </t-tag>
            </template>
            <span v-else class="text-xs text-[var(--td-text-color-placeholder)]">-</span>
          </div>
        </template>

        <template #shortId="{ row }">
          <span class="text-xs font-mono text-[var(--td-text-color-secondary)]">{{ row.shortId }}</span>
        </template>

        <template #size="{ row }">
          <span class="text-xs font-mono">{{ row.size || '-' }}</span>
        </template>

        <template #createdAt="{ row }">
          <span class="text-xs text-[var(--td-text-color-secondary)]">{{ row.createdAt || '-' }}</span>
        </template>

        <template #usedBy="{ row }">
          <div v-if="row.usedBy?.length" class="flex flex-wrap gap-1">
            <t-tag
              v-for="usage in row.usedBy"
              :key="usage.instanceId"
              theme="success"
              variant="light"
              size="small"
              :title="`配置值：${usage.configuredImage}`"
            >
              #{{ usage.instanceId }} {{ usage.instanceName }}
            </t-tag>
          </div>
          <span v-else class="text-xs text-[var(--td-text-color-placeholder)]">未被引用</span>
        </template>

        <template #op="{ row }">
          <div class="flex items-center gap-1">
            <t-button
              v-if="!row.isDangling && updateStatusMap[row.reference]?.hasUpdate"
              theme="warning"
              variant="text"
              size="small"
              @click="openPullDialog(row.reference)"
            >
              <template #icon><cloud-download-icon /></template>
              更新
            </t-button>
            <t-button
              theme="primary"
              variant="text"
              size="small"
              @click="openInspect(row.isDangling ? row.imageId : row.reference)"
            >
              <template #icon><info-circle-icon /></template>
              详情
            </t-button>
            <t-button variant="text" size="small" @click="copyText(row.isDangling ? row.imageId : row.reference)">
              <template #icon><link-icon /></template>
              复制
            </t-button>
            <t-button theme="danger" variant="text" size="small" @click="handleDelete(row)">
              <template #icon><delete-icon /></template>
              删除
            </t-button>
          </div>
        </template>
      </t-table>
    </div>

    <pull-image-dialog v-model:visible="pullVisible" :preset-image="pullPreset" @finished="handlePullFinished" />
    <image-inspect-dialog v-model:visible="inspectVisible" :reference="inspectReference" />
  </div>
</template>
