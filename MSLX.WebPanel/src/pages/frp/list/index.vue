<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { CloudIcon, DeleteIcon, RefreshIcon } from 'tdesign-icons-vue-next';

import Result from '@/components/result/index.vue';

import type { FrpListModel } from '@/api/model/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { getFrpAutoStartList, postChangeFrpAutoStartList, postDeleteFrpTunnel } from '@/api/frp';
import { DialogPlugin, MessagePlugin } from 'tdesign-vue-next';
import { useUserStore } from '@/store';

const tunnelsStore = useTunnelsStore();
const userStore = useUserStore();

const loading = ref(true);
const isError = ref(false);

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
  } catch (error) {
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

async function getList() {
  try {
    loading.value = true;
    isError.value = false; // 每次请求前重置错误状态
    await tunnelsStore.getTunnels();
  } catch (error) {
    console.error(error);
    isError.value = true; // 标记发生错误
  } finally {
    loading.value = false;
  }
}

const handleCardClick = (item: FrpListModel) => {
  changeUrl(`/frp/console/${item.id}`);
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
      } catch (error) {
        MessagePlugin.error(error.message);
      }
    },
    onClose: () => {
      confirmDialog.hide();
    },
  });
};

onMounted(() => {
  getList();
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-zinc-800 dark:text-zinc-200 pb-5">
    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-zinc-900 dark:text-zinc-100 m-0">隧道列表</h2>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0">管理您的 FRP 隧道映射，设置自启动并监控运行状态</p>
      </div>

      <div class="flex items-center gap-2 sm:gap-3 flex-wrap">
        <t-button variant="dashed" @click="getList">
          <template #icon><refresh-icon /></template>
          刷新
        </t-button>
        <t-button v-if="userStore.isAdmin" variant="outline" @click="openAutoStartSettings"> 自启动设置 </t-button>
        <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/frp/create')"> 创建隧道 </t-button>
      </div>
    </div>

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
        class="flex flex-col items-center justify-center py-24 design-card bg-white/40 dark:bg-zinc-800/40 rounded-2xl border-2 border-dashed border-zinc-200/50 dark:border-zinc-700/50"
      >
        <result title="暂无隧道" :tip="userStore.isAdmin ? '快去创建一个吧' : '管理员尚未为您分配隧道'" type="404">
          <t-button v-if="userStore.isAdmin" theme="primary" @click="changeUrl('/frp/create')">立即创建</t-button>
        </result>
      </div>

      <div v-else class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
        <div
          v-for="(item, index) in tunnelsStore.frpList"
          :key="item.id"
          :style="{ '--i': index }"
          class="design-card group flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm hover:shadow-md hover:border-[var(--color-primary)]/50 transition-all duration-300 p-5 gap-5 cursor-pointer"
          @click="handleCardClick(item)"
        >
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
              <h4 class="text-base font-bold text-zinc-900 dark:text-zinc-100 truncate tracking-tight">
                {{ item.name }}
              </h4>
            </div>
            <span class="text-xs font-mono text-zinc-400 dark:text-zinc-500 shrink-0 opacity-60">#{{ item.id }}</span>
          </div>

          <div class="flex items-end justify-between px-0.5">
            <div class="flex flex-col gap-1.5">
              <span class="text-[10px] text-zinc-400 dark:text-zinc-500 uppercase tracking-widest font-black opacity-80">协议类型</span>
              <div class="flex items-center gap-2 text-zinc-800 dark:text-zinc-200">
                <cloud-icon size="16px" class="text-[var(--color-primary)] opacity-70" />
                <span class="text-sm font-bold">{{ item.service }}</span>
              </div>
            </div>

            <div class="flex flex-col items-end gap-1.5">
              <span class="text-[10px] text-zinc-400 dark:text-zinc-500 uppercase tracking-widest font-black opacity-80">配置格式</span>
              <t-tag
                size="small"
                :theme="getConfigTheme(item.configType) as any"
                variant="light"
                shape="round"
                class="!px-3 !h-5 !text-[10px] font-black italic tracking-tighter"
              >
                {{ item.configType.toUpperCase() }}
              </t-tag>
            </div>
          </div>

          <div class="flex items-center justify-between pt-4 mt-auto border-t border-dashed border-zinc-200/60 dark:border-zinc-700/60">
      <span class="text-[11px] text-zinc-400 dark:text-zinc-500 group-hover:text-[var(--color-primary)] transition-colors font-bold">
        隧道控制台 →
      </span>
            <div class="flex items-center gap-1">
              <t-button
                v-if="userStore.isAdmin"
                shape="circle"
                theme="danger"
                variant="text"
                class="hover:!bg-red-500/10"
                @click.stop="handleDelete(item.id)"
              >
                <template #icon><delete-icon size="16" /></template>
              </t-button>
            </div>
          </div>
        </div>
      </div>
    </div>

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
              <p class="text-zinc-900 dark:text-zinc-100 font-bold mb-1">自启动策略说明</p>
              <p class="text-zinc-500 dark:text-zinc-400 leading-relaxed m-0">
                勾选的隧道将在 MSLFrp 守护进程启动时自动加载并运行。
              </p>
            </div>
          </div>

          <div v-if="tunnelsStore.frpList.length > 0">
            <t-checkbox-group v-model="autoStartState.selectedIds" class="w-full">
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div
                  v-for="item in tunnelsStore.frpList"
                  :key="item.id"
                  class="p-3 bg-zinc-50/50 dark:bg-zinc-800/40 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 hover:bg-zinc-100 dark:hover:bg-zinc-700/60 transition-colors"
                >
                  <t-checkbox :value="item.id" class="!w-full">
                    <div class="flex items-center justify-between w-full ml-1 overflow-hidden">
                      <span class="text-sm font-medium text-zinc-800 dark:text-zinc-200 truncate pr-2">{{
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
.grid > div {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(12px);
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
