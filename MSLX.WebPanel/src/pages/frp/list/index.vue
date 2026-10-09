<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import {
  AddIcon,
  CloudIcon,
  DeleteIcon,
  EditIcon,
  LayersIcon,
  PlayCircleIcon,
  RefreshIcon,
  RocketIcon,
  StopCircleIcon,
  TagIcon,
  ViewModuleIcon,
} from 'tdesign-icons-vue-next';

import Result from '@/components/result/index.vue';

import type { FrpListModel } from '@/api/model/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import {
  getFrpAutoStartList,
  postChangeFrpAutoStartList,
  postDeleteFrpTunnel,
  postFrpAction,
  postUpdateFrpTunnel,
} from '@/api/frp';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';
import { useUserStore, useNodeStore } from '@/store';
import NodeSwitcher from '@/components/node-switcher/index.vue';

const tunnelsStore = useTunnelsStore();
const userStore = useUserStore();
const nodeStore = useNodeStore();

const loading = ref(true);
const isError = ref(false);

// ================= 视图模式切换 (平铺 / 分类) =================
// 默认平铺视图 ('flat')，分类视图 ('category')
const viewMode = ref<'flat' | 'category'>(
  (localStorage.getItem('mslx_tunnel_view_mode') as 'flat' | 'category') || 'flat'
);

const toggleViewMode = () => {
  viewMode.value = viewMode.value === 'flat' ? 'category' : 'flat';
  localStorage.setItem('mslx_tunnel_view_mode', viewMode.value);
  MessagePlugin.info(viewMode.value === 'flat' ? '已切换为平铺视图' : '已切换为分类视图');
};

const handleNodeChange = () => {
  getList();
};

watch(() => nodeStore.activeNodeId, () => {
  getList();
});

// 自启动列表数据
const autoStartState = reactive({
  visible: false,
  loading: false,
  submitting: false,
  selectedIds: [] as number[],
});

// 打开设置弹窗
const openAutoStartSettings = async () => {
  autoStartState.visible = true;
  autoStartState.loading = true;
  try {
    if (tunnelsStore.frpList.length === 0) {
      await tunnelsStore.getTunnels();
    }
    const res = await getFrpAutoStartList();
    autoStartState.selectedIds = res || [];
  } catch (error: any) {
    MessagePlugin.error('获取自启动配置失败 ' + error.message);
  } finally {
    autoStartState.loading = false;
  }
};

// 保存设置
const handleSaveAutoStart = async () => {
  autoStartState.submitting = true;
  try {
    await postChangeFrpAutoStartList(autoStartState.selectedIds);
    MessagePlugin.success('自启动设置已更新');
    autoStartState.visible = false;
  } catch (error: any) {
    MessagePlugin.error('保存失败: ' + error.message);
  } finally {
    autoStartState.submitting = false;
  }
};

// 配置文件 → 颜色
const getConfigTheme = (type: string) => {
  const map: Record<string, string> = {
    toml: 'primary',
    ini: 'warning',
    cmd: 'danger',
    json: 'success',
  };
  return map[type] || 'default';
};

// 标签色彩哈希映射
const getTagTheme = (tag: string) => {
  const themes = ['primary', 'success', 'warning', 'danger'];
  let hash = 0;
  for (let i = 0; i < tag.length; i++) {
    hash = (hash << 5) - hash + tag.charCodeAt(i);
  }
  return themes[Math.abs(hash) % themes.length];
};

async function getList() {
  try {
    loading.value = true;
    isError.value = false;
    await tunnelsStore.getTunnels();
  } catch (error) {
    console.error(error);
    isError.value = true;
  } finally {
    loading.value = false;
  }
}

const handleCardClick = (e: Event, item: FrpListModel, newTab = false) => {
  changeUrl(`/frp/console/${item.id}`, newTab);
};

const handleDelete = (id: number) => {
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除隧道?',
    body: '删除后该隧道将无法恢复。确定要继续吗？',
    theme: 'danger',
    onConfirm: async () => {
      try {
        await postDeleteFrpTunnel(id);
        MessagePlugin.success(`隧道 ${id} 删除成功`);
        await getList();
        confirmDialog.hide();
      } catch (error: any) {
        MessagePlugin.error(error.message);
      }
    },
    onClose: () => {
      confirmDialog.hide();
    },
  });
};

// ================= 标签管理与分类数据 =================
const activeTagFilter = ref<string>('all');

// 提取当前所有不重复的标签
const allExistingTags = computed(() => {
  const set = new Set<string>();
  tunnelsStore.frpList.forEach((item) => {
    (item.tags || []).forEach((t) => {
      if (t && t.trim()) set.add(t.trim());
    });
  });
  return Array.from(set);
});

const untaggedCount = computed(
  () => tunnelsStore.frpList.filter((i) => !i.tags || i.tags.length === 0).length
);

// 编辑下拉选项
const tagSelectOptions = computed(() =>
  allExistingTags.value.map((t) => ({ label: t, value: t }))
);

interface TunnelGroup {
  key: string;
  label: string;
  items: FrpListModel[];
  runningCount: number;
  totalCount: number;
}

// 分组计算
const tunnelGroups = computed<TunnelGroup[]>(() => {
  const list = tunnelsStore.frpList;
  if (allExistingTags.value.length === 0) {
    return [
      {
        key: 'all',
        label: '全部隧道',
        items: list,
        runningCount: list.filter((i) => i.status).length,
        totalCount: list.length,
      },
    ];
  }

  const groups: TunnelGroup[] = [];

  // 各个具体标签分组
  allExistingTags.value.forEach((tag) => {
    const taggedItems = list.filter((i) => (i.tags || []).includes(tag));
    if (taggedItems.length > 0) {
      groups.push({
        key: tag,
        label: tag,
        items: taggedItems,
        runningCount: taggedItems.filter((i) => i.status).length,
        totalCount: taggedItems.length,
      });
    }
  });

  // 未分类隧道 (没有 tags 或 tags 为空)
  const untagged = list.filter((i) => !i.tags || i.tags.length === 0);
  if (untagged.length > 0) {
    groups.push({
      key: '__untagged__',
      label: '未分类',
      items: untagged,
      runningCount: untagged.filter((i) => i.status).length,
      totalCount: untagged.length,
    });
  }

  return groups;
});

// 根据视图模式与筛选过滤展示的分组
const filteredGroups = computed(() => {
  // 平铺视图模式
  if (viewMode.value === 'flat') {
    return [
      {
        key: 'all',
        label: '',
        items: tunnelsStore.frpList,
        runningCount: tunnelsStore.frpList.filter((i) => i.status).length,
        totalCount: tunnelsStore.frpList.length,
      },
    ];
  }

  // 分类视图模式：根据标签筛选返回对应组
  if (activeTagFilter.value === 'all') {
    return tunnelGroups.value;
  }
  return tunnelGroups.value.filter((g) => g.key === activeTagFilter.value);
});

// ================= 并发批量操作 =================
const batchLoading = ref(false);

const handleBatchAction = async (action: 'start' | 'stop', targetGroup: TunnelGroup) => {
  const targetItems = targetGroup.items.filter((item) =>
    action === 'start' ? !item.status : item.status
  );

  const actionText = action === 'start' ? '启动' : '停止';

  if (targetItems.length === 0) {
    MessagePlugin.info(
      action === 'start'
        ? `【${targetGroup.label || '当前'}】分组下的隧道均已在运行中`
        : `【${targetGroup.label || '当前'}】分组下的隧道均已停止`
    );
    return;
  }

  const confirmDialog = DialogPlugin.confirm({
    header: `确认批量${actionText}【${targetGroup.label || '选定'}】隧道?`,
    body: `即将批量${actionText}该分组下的 ${targetItems.length} 条隧道，是否继续？`,
    theme: action === 'stop' ? 'warning' : 'info',
    onConfirm: async () => {
      confirmDialog.hide();
      batchLoading.value = true;
      const msg = MessagePlugin.loading(`正在批量${actionText}【${targetGroup.label || ''}】隧道中...`);
      try {
        const promises = targetItems.map((item) => postFrpAction(action, item.id));
        const results = await Promise.allSettled(promises);
        const rejected = results.filter((r) => r.status === 'rejected');

        if (rejected.length > 0) {
          MessagePlugin.warning({
            content: `操作完成，有 ${rejected.length} 条隧道${actionText}失败`,
            duration: 4000,
          });
        } else {
          MessagePlugin.success(`已成功批量${actionText} ${targetItems.length} 条隧道`);
        }
        await getList();
      } catch (e: any) {
        MessagePlugin.error(`批量操作出现异常: ${e.message}`);
      } finally {
        MessagePlugin.close(msg);
        batchLoading.value = false;
      }
    },
  });
};

// ================= 编辑隧道名称与标签 =================
const editState = reactive({
  visible: false,
  submitting: false,
  id: 0,
  name: '',
  tags: [] as string[],
});

const openEditDialog = (item: FrpListModel) => {
  editState.id = item.id;
  editState.name = item.name;
  editState.tags = item.tags ? [...item.tags] : [];
  editState.visible = true;
};

const handleSaveEdit = async () => {
  if (!editState.name.trim()) {
    MessagePlugin.warning('隧道名称不能为空');
    return;
  }
  editState.submitting = true;
  try {
    await postUpdateFrpTunnel(editState.id, editState.name.trim(), editState.tags);
    MessagePlugin.success('隧道信息更新成功');
    editState.visible = false;
    await getList();
  } catch (e: any) {
    MessagePlugin.error(`保存失败: ${e.message}`);
  } finally {
    editState.submitting = false;
  }
};

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <!-- 顶部统一控制卡片 -->
    <div
      class="design-card flex flex-col gap-4 p-5 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
    >
      <!-- 主标题与全局操作行 -->
      <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div class="flex flex-col gap-1 items-start">
          <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0">隧道列表</h2>
          <p class="text-sm text-[var(--td-text-color-secondary)] m-0">管理您的 FRP 隧道映射，支持打标签分类并批量启停</p>
        </div>

        <div class="flex items-center gap-2 sm:gap-3 flex-wrap">
          <node-switcher @change="handleNodeChange" />

          <!-- 视图切换小图标按钮 -->
          <t-tooltip :content="viewMode === 'flat' ? '切换为分类视图' : '切换为平铺视图'">
            <t-button variant="dashed" shape="square" @click="toggleViewMode">
              <template #icon>
                <layers-icon v-if="viewMode === 'flat'" />
                <view-module-icon v-else />
              </template>
            </t-button>
          </t-tooltip>

          <t-button variant="dashed" @click="getList">
            <template #icon><refresh-icon /></template>
            刷新
          </t-button>
          <t-button v-if="userStore.isAdmin" variant="outline" @click="openAutoStartSettings">
            <template #icon><rocket-icon /></template>
            自启动设置
          </t-button>
          <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/frp/create')" @auxclick.middle.prevent="changeUrl('/frp/create', true)">
            <template #icon><add-icon /></template>
            创建隧道
          </t-button>
        </div>
      </div>

      <!-- 分类筛选栏  -->
      <div
        v-if="viewMode === 'category' && allExistingTags.length > 0 && !loading && !isError && tunnelsStore.frpList.length > 0"
        class="pt-3.5 border-t border-dashed border-[var(--td-component-border)]/80 flex items-center justify-between gap-3 overflow-x-auto scrollbar-none flex-wrap w-full"
      >
        <div class="flex items-center gap-2.5 flex-wrap">
          <span class="text-xs font-bold text-[var(--td-text-color-secondary)] uppercase tracking-wider flex items-center gap-1.5 shrink-0">
            <tag-icon size="14" class="text-[var(--color-primary)]" />
            分类:
          </span>
          <t-button
            size="small"
            :variant="activeTagFilter === 'all' ? 'base' : 'outline'"
            :theme="activeTagFilter === 'all' ? 'primary' : 'default'"
            shape="round"
            class="!text-xs"
            @click="activeTagFilter = 'all'"
          >
            全部 ({{ tunnelsStore.frpList.length }})
          </t-button>
          <t-button
            v-for="tag in allExistingTags"
            :key="tag"
            size="small"
            :variant="activeTagFilter === tag ? 'base' : 'outline'"
            :theme="activeTagFilter === tag ? 'primary' : 'default'"
            shape="round"
            class="!text-xs"
            @click="activeTagFilter = tag"
          >
            {{ tag }} ({{ tunnelsStore.frpList.filter((i) => (i.tags || []).includes(tag)).length }})
          </t-button>
          <t-button
            v-if="untaggedCount > 0"
            size="small"
            :variant="activeTagFilter === '__untagged__' ? 'base' : 'outline'"
            :theme="activeTagFilter === '__untagged__' ? 'primary' : 'default'"
            shape="round"
            class="!text-xs"
            @click="activeTagFilter = '__untagged__'"
          >
            未分类 ({{ untaggedCount }})
          </t-button>
        </div>
      </div>
    </div>

    <!-- 核心列表展示区域 -->
    <div class="relative min-h-[400px]">
      <div v-if="loading" class="flex flex-col items-center justify-center py-24">
        <t-loading size="medium" text="正在获取隧道信息..." />
      </div>

      <div
        v-else-if="isError"
        class="flex flex-col items-center justify-center py-16 design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border border-red-500/20"
      >
        <result title="数据获取失败" tip="无法连接到服务器，请检查网络" type="500">
          <t-button theme="primary" @click="getList">重试</t-button>
        </result>
      </div>

      <div
        v-else-if="tunnelsStore.frpList.length === 0"
        class="flex flex-col items-center justify-center py-24 design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-[var(--td-component-border)]"
      >
        <result title="暂无隧道" :tip="userStore.isAdmin ? '快去创建一个吧' : '管理员尚未为您分配隧道'" type="404">
          <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/frp/create')" @auxclick.middle.prevent="changeUrl('/frp/create', true)">立即创建</t-button>
        </result>
      </div>

      <!-- 列表内容：平铺视图直接渲染卡片网格；分类视图按分组划分 -->
      <div v-else class="flex flex-col gap-6">
        <div
          v-for="group in filteredGroups"
          :key="group.key"
          class="flex flex-col gap-3.5"
        >
          <!-- 分类视图下的分组标题卡片 (仅在分类模式下显示) -->
          <div
            v-if="viewMode === 'category'"
            class="design-card flex items-center justify-between px-5 py-3 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left backdrop-blur-sm"
          >
            <div class="flex items-center gap-3">
              <span class="w-1.5 h-4 rounded-full bg-[var(--color-primary)]"></span>
              <h3 class="text-base font-bold text-[var(--td-text-color-primary)] m-0 flex items-center gap-2.5 tracking-tight">
                {{ group.label }}
              </h3>
              <span class="text-xs px-2.5 py-0.5 rounded-full bg-[var(--td-bg-color-secondarycontainer)] text-[var(--td-text-color-secondary)] font-medium">
                {{ group.runningCount }}/{{ group.totalCount }} 运行中
              </span>
            </div>
            <div class="flex items-center gap-2">
              <t-button
                size="small"
                variant="outline"
                theme="primary"
                shape="round"
                :disabled="batchLoading || group.runningCount === group.totalCount"
                @click="handleBatchAction('start', group)"
              >
                <template #icon><play-circle-icon size="14" /></template>
                全部启动
              </t-button>
              <t-button
                size="small"
                variant="outline"
                theme="danger"
                shape="round"
                :disabled="batchLoading || group.runningCount === 0"
                @click="handleBatchAction('stop', group)"
              >
                <template #icon><stop-circle-icon size="14" /></template>
                全部停止
              </t-button>
            </div>
          </div>

          <!-- 卡片网格 -->
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
            <div
              v-for="(item, index) in group.items"
              :key="item.id"
              :style="{ animationDelay: `${index * 0.05}s` }"
              class="list-item-anim design-card group flex flex-col bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-4 cursor-pointer"
              @click="handleCardClick($event, item)"
              @auxclick.middle.prevent="handleCardClick($event, item, true)"
            >
              <div class="flex flex-col gap-2">
                <div class="flex items-center justify-between gap-3">
                  <div class="flex items-center gap-2.5 min-w-0">
                    <div class="relative flex items-center justify-center shrink-0">
                      <span
                        v-if="item.status"
                        class="absolute w-2.5 h-2.5 bg-emerald-400 rounded-full animate-ping opacity-75"
                      ></span>
                      <span
                        :class="item.status ? 'bg-emerald-500' : 'bg-zinc-300 dark:bg-zinc-600'"
                        class="relative w-2 h-2 rounded-full"
                      ></span>
                    </div>

                    <h4 class="text-base font-bold text-[var(--td-text-color-primary)] truncate tracking-tight">
                      {{ item.name }}
                    </h4>
                    <span class="text-xs font-mono text-[var(--td-text-color-secondary)] ml-1 opacity-60 shrink-0"
                      >#{{ item.id }}</span
                    >
                  </div>
                </div>

                <!-- 标签展示区 -->
                <div v-if="item.tags && item.tags.length > 0" class="flex flex-wrap gap-1.5 pt-0.5">
                  <t-tag
                    v-for="tag in item.tags"
                    :key="tag"
                    size="small"
                    variant="light"
                    :theme="getTagTheme(tag) as any"
                    class="!text-[11px] !px-2 !h-5 !font-medium"
                  >
                    {{ tag }}
                  </t-tag>
                </div>
              </div>

              <div class="flex items-center gap-8 px-0.5 mt-auto">
                <div class="flex flex-col gap-1.5">
                  <span
                    class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                    >提供商</span
                  >
                  <div class="flex items-center gap-2 text-[var(--td-text-color-primary)]">
                    <cloud-icon size="16px" class="text-[var(--color-primary)] opacity-70" />
                    <span class="text-sm font-bold leading-none">{{ item.service }}</span>
                  </div>
                </div>

                <div class="flex flex-col gap-1.5">
                  <span
                    class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black opacity-80"
                    >配置格式</span
                  >
                  <div>
                    <t-tag
                      size="small"
                      :theme="getConfigTheme(item.configType) as any"
                      variant="light-outline"
                      class="!px-3 !h-5 !text-[10px] font-black italic tracking-tighter border-zinc-200 dark:border-zinc-700"
                    >
                      {{ item.configType.toUpperCase() }}
                    </t-tag>
                  </div>
                </div>
              </div>

              <!-- 卡片底部操作栏 -->
              <div
                class="flex items-center justify-between pt-3 mt-1 border-t border-dashed border-zinc-200/60 dark:border-zinc-700/60"
              >
                <span
                  class="text-xs text-[var(--td-text-color-secondary)] group-hover:text-[var(--color-primary)] transition-colors font-bold"
                >
                  隧道控制台 →
                </span>
                <div class="flex items-center gap-1">
                  <t-button
                    v-if="userStore.isAdmin"
                    shape="circle"
                    theme="default"
                    variant="text"
                    size="small"
                    class="hover:!bg-zinc-500/10"
                    title="编辑名称与标签"
                    @click.stop="openEditDialog(item)"
                  >
                    <template #icon><edit-icon size="15" /></template>
                  </t-button>
                  <t-button
                    v-if="userStore.isAdmin"
                    shape="circle"
                    theme="danger"
                    variant="text"
                    size="small"
                    class="hover:!bg-red-500/10"
                    title="删除隧道"
                    @click.stop="handleDelete(item.id)"
                  >
                    <template #icon><delete-icon size="16" /></template>
                  </t-button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 编辑隧道名称与标签弹窗 -->
    <t-dialog
      v-model:visible="editState.visible"
      header="编辑隧道信息"
      width="520px"
      :confirm-btn="{ content: '保存修改', loading: editState.submitting }"
      @confirm="handleSaveEdit"
    >
      <div class="flex flex-col gap-4 py-2">
        <div class="flex flex-col gap-1.5">
          <label class="text-xs text-[var(--td-text-color-secondary)] font-medium">隧道名称</label>
          <t-input v-model="editState.name" placeholder="请输入隧道名称" clearable />
        </div>
        <div class="flex flex-col gap-1.5">
          <label class="text-xs text-[var(--td-text-color-secondary)] font-medium">
            隧道标签 <span class="text-[var(--td-text-color-placeholder)] font-normal">(支持下拉选择已有标签，或输入新标签并回车添加)</span>
          </label>
          <t-select
            v-model="editState.tags"
            multiple
            filterable
            creatable
            placeholder="请选择或输入标签后按回车"
            :options="tagSelectOptions"
            clearable
          />
        </div>
      </div>
    </t-dialog>

    <!-- 自启动设置弹窗 -->
    <t-dialog
      v-model:visible="autoStartState.visible"
      header="设置开机自启动隧道"
      width="640px"
      :confirm-btn="{ content: '保存设置', loading: autoStartState.submitting }"
      @confirm="handleSaveAutoStart"
    >
      <div class="delete-dialog-body min-h-[200px]">
        <t-loading :loading="autoStartState.loading" text="读取配置中..." size="small">
          <div class="alert-zinc bg-primary/5 border border-primary/20 p-4 rounded-xl mb-6 flex items-start gap-3">
            <t-icon name="info-circle-filled" class="text-primary mt-0.5" />
            <div class="text-sm">
              <p class="text-[var(--td-text-color-primary)] font-bold mb-1">自启动策略说明</p>
              <p class="text-[var(--td-text-color-secondary)] leading-relaxed m-0">
                勾选的隧道将在 MSLX 守护进程启动时自动加载并运行。
              </p>
            </div>
          </div>

          <div v-if="tunnelsStore.frpList.length > 0">
            <t-checkbox-group v-model="autoStartState.selectedIds" class="w-full">
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div
                  v-for="item in tunnelsStore.frpList"
                  :key="item.id"
                  class="p-3 bg-zinc-50/50 dark:bg-zinc-800/40 rounded-xl border border-[var(--td-component-border)] hover:bg-zinc-100 dark:hover:bg-zinc-700/60 transition-colors"
                >
                  <t-checkbox :value="item.id" class="!w-full">
                    <div class="flex items-center justify-between w-full ml-1 overflow-hidden">
                      <span class="text-sm font-medium text-[var(--td-text-color-primary)] truncate pr-2">{{
                        item.name
                      }}</span>
                      <span class="text-[10px] font-mono text-zinc-400 shrink-0">#{{ item.id }}</span>
                    </div>
                  </t-checkbox>
                </div>
              </div>
            </t-checkbox-group>
          </div>
          <div v-else class="py-12 text-center text-zinc-400 italic">暂无可用隧道</div>
        </t-loading>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped>
@reference "@/style/tailwind/index.css";

/* 列表进场动画 */
.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  content-visibility: auto;
  contain-intrinsic-size: auto 150px;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* 深度适配 TDesign 属性调整 */
:deep(.t-avatar) {
  @apply ring-1 ring-zinc-200/50 dark:ring-zinc-700/50;
}

:deep(.t-dialog) {
  @apply !rounded-2xl shadow-2xl;
}

:deep(.t-checkbox__label) {
  @apply !w-full;
}
</style>
