<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  CheckCircleIcon,
  CodeIcon,
  GitCommitIcon,
  HistoryIcon,
  TimeIcon,
  UserCircleIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import type { BuildInfoModel } from '@/api/model/buildInfo';
import { getBuildInfo } from '@/api/buildInfo';
import { UpdateLogDetailModel } from '@/api/mslapi/model/updateLog';
import { getMSLXUpdateLog } from '@/api/mslapi/updateLog';
import HurryUpppppppp from '@/components/HurryUpppppppp.vue';

const developers = [
  {
    name: 'xiaoyu',
    role: 'Core Developer',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=1791123970&spec=640&img_type=jpg',
    desc: '核心开发者',
  },
  {
    name: 'Weheal',
    role: 'Core Developer',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=2035582067&spec=640&img_type=jpg',
    desc: '核心开发者',
  },
];

const testers = [
  {
    name: 'GuHanDuRen',
    role: 'Alpha Tester',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=2778318425&spec=640&img_type=jpg',
    desc: '最早期内部功能测试',
  },
  {
    name: '邱息',
    role: 'Beta Tester',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=3687624214&spec=640&img_type=jpg',
    desc: '提供了宝贵的建议',
  },
  {
    name: 'Nebula琳',
    role: 'Beta Tester',
    avatar: 'https://q.qlogo.cn/headimg_dl?dst_uin=3770298358&spec=640&img_type=jpg',
    desc: '提供了宝贵的建议',
  },
  {
    name: 'MSLX Beta 群友们',
    role: 'Members',
    avatar: 'https://p.qlogo.cn/gh/839645854/839645854/0',
    desc: '感谢各位内测群的群友们！',
  },
];

const loading = ref(true);
const buildInfo = ref<BuildInfoModel | null>(null);

// 更新日志的状态
const logLoading = ref(true);
const updateLogs = ref<UpdateLogDetailModel[]>([]);

const fetchBuildInfo = async () => {
  try {
    loading.value = true;
    buildInfo.value = await getBuildInfo();
  } catch (e) {
    console.error(e);
    if (process.env.NODE_ENV !== 'development') {
      MessagePlugin.warning('无法加载构建信息');
    }
  } finally {
    loading.value = false;
  }
};

// 获取更新日志的方法
const fetchUpdateLogs = async () => {
  try {
    logLoading.value = true;
    const res = await getMSLXUpdateLog();
    if (res) {
      updateLogs.value = res as unknown as UpdateLogDetailModel[];
    }
  } catch (e) {
    console.error('获取更新日志失败:', e);
  } finally {
    logLoading.value = false;
  }
};

const dependenciesList = computed(() => {
  if (!buildInfo.value?.dependencies) return [];
  return Object.entries(buildInfo.value.dependencies).map(([k, v]) => ({ name: k, version: v }));
});

onMounted(() => {
  fetchBuildInfo();
  fetchUpdateLogs();
});
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-zinc-800 dark:text-zinc-200 pb-5">

    <div
      class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm text-left"
    >
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-zinc-900 dark:text-zinc-100 m-0">关于与更新日志</h2>
        <p class="text-sm text-zinc-500 dark:text-zinc-400 m-0">
          了解 MSLX 的前世今生、幕后团队以及系统构建与更新信息
        </p>
      </div>

      <div class="flex items-center gap-3">
        <img src="@/assets/logo.png" width="48px" alt="logo"/>
      </div>
    </div>

    <div class="relative min-h-[400px]">
      <div class="flex flex-col gap-5">

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center justify-between">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">关于 MSLX</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">全新一代跨平台开服工具</span>
              </div>
            </div>
          </div>

          <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-5 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 text-sm leading-relaxed text-zinc-700 dark:text-zinc-300 shadow-sm transition-colors hover:bg-white dark:hover:bg-zinc-800">
            <p class="mb-3">
              <strong class="text-[var(--color-primary)] font-extrabold tracking-wide">MSLX</strong> 是由 <strong class="text-zinc-900 dark:text-zinc-100 font-bold">MSL 原班团队 MSLTeam</strong> 倾力打造的全新一代开服工具。 基于
              <span class="inline-flex items-center px-1.5 py-0.5 rounded-md bg-[#512bd4]/10 text-[#512bd4] dark:text-[#a084fb] font-bold text-xs mx-1">.NET Core 10.0</span> 环境。
            </p>
            <p class="m-0">
              它传承了 MSL 经典的 UI 设计语言，旨在让操作零门槛——无论是老用户还是新伙伴，都能即刻上手，极速部署您的 MC 服务器。 MSLX 不仅 <strong class="text-[var(--color-primary)] font-bold">完美支持跨平台</strong> (Windows / macOS / Linux) 运行，相比前代，更引入了强大的
              <strong class="text-[var(--color-primary)] font-bold">远程访问</strong> 功能，让管理更自由。
            </p>
          </div>
        </div>

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0.05s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">开发团队</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">感谢以下开发者对本项目的杰出贡献</span>
              </div>
            </div>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div v-for="dev in developers" :key="dev.name" class="group flex items-center bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 transition-all duration-300 hover:-translate-y-1 hover:shadow-md hover:border-[var(--color-primary)]/50 hover:bg-white dark:hover:bg-zinc-800">
              <t-avatar :image="dev.avatar" size="56px" shape="circle" class="shrink-0 ring-2 ring-white dark:ring-zinc-700 shadow-sm transition-transform group-hover:scale-105" />
              <div class="ml-4 flex-1 min-w-0">
                <div class="font-bold text-base text-zinc-900 dark:text-zinc-100 truncate">{{ dev.name }}</div>
                <div class="text-[10px] font-extrabold px-2 py-0.5 rounded bg-[var(--color-primary)]/10 text-[var(--color-primary)] inline-block mt-0.5 mb-1 tracking-wider uppercase">{{ dev.role }}</div>
                <div class="text-xs text-zinc-500 dark:text-zinc-400 truncate font-medium" :title="dev.desc">{{ dev.desc }}</div>
              </div>
            </div>
          </div>
        </div>

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0.1s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-warning)] rounded-full shadow-[0_0_8px_var(--color-warning)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">催更？</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">请点击下方按钮......</span>
              </div>
            </div>
          </div>
          <hurry-upppppppp />
        </div>

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0.15s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-success)] rounded-full shadow-[0_0_8px_var(--color-success)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">鸣谢</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">特别感谢参与内测并提供宝贵反馈的伙伴们</span>
              </div>
            </div>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
            <div v-for="tester in testers" :key="tester.name" class="group flex items-center bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 transition-all duration-300 hover:-translate-y-1 hover:shadow-md hover:border-[var(--color-success)]/50 hover:bg-white dark:hover:bg-zinc-800">
              <t-avatar :image="tester.avatar" size="56px" shape="circle" class="shrink-0 ring-2 ring-white dark:ring-zinc-700 shadow-sm transition-transform group-hover:scale-105" />
              <div class="ml-4 flex-1 min-w-0">
                <div class="font-bold text-base text-zinc-900 dark:text-zinc-100 truncate">{{ tester.name }}</div>
                <div class="text-[10px] font-extrabold px-2 py-0.5 rounded bg-[var(--color-success)]/10 text-[var(--color-success)] inline-block mt-0.5 mb-1 tracking-wider uppercase">{{ tester.role }}</div>
                <div class="text-xs text-zinc-500 dark:text-zinc-400 truncate font-medium" :title="tester.desc">{{ tester.desc }}</div>
              </div>
            </div>
          </div>
        </div>

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0.2s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex items-center justify-between">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">更新日志</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">查看 MSLX 历史版本的所有改动记录</span>
              </div>
            </div>
            <t-loading v-if="logLoading" size="small" />
          </div>

          <div v-if="updateLogs.length > 0" class="max-h-[400px] overflow-y-auto custom-scrollbar pr-2">
            <t-timeline>
              <t-timeline-item v-for="(log, index) in updateLogs" :key="index" dot-color="primary">
                <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 ml-1 transition-colors hover:bg-white dark:hover:bg-zinc-800">
                  <div class="flex items-center gap-3 mb-2">
                    <span class="inline-flex items-center px-2 py-0.5 rounded-md bg-[var(--color-primary)]/10 text-[var(--color-primary)] font-extrabold text-xs tracking-wider border border-[var(--color-primary)]/20">
                      {{ log.version }}
                    </span>
                    <div class="flex items-center gap-1 text-xs text-zinc-500 dark:text-zinc-400 font-mono font-medium">
                      <time-icon size="14px" />
                      {{ log.time }}
                    </div>
                  </div>
                  <div class="text-sm text-zinc-700 dark:text-zinc-300 whitespace-pre-wrap leading-relaxed">{{ log.changes }}</div>
                </div>
              </t-timeline-item>
            </t-timeline>
          </div>
          <div v-else-if="!logLoading" class="flex justify-center items-center py-10">
            <span class="text-sm font-medium text-zinc-400 dark:text-zinc-500 bg-zinc-50 dark:bg-zinc-800/50 px-4 py-2 rounded-full border border-zinc-200/50 dark:border-zinc-700/50">暂无更新日志</span>
          </div>
        </div>

        <div class="design-card list-item-anim flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm p-6" style="animation-delay: 0.25s;">
          <div class="mb-5 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div class="flex items-center gap-3">
              <div class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
              <div class="flex flex-col">
                <h2 class="text-lg font-bold text-zinc-900 dark:text-zinc-100 m-0 leading-none">构建信息</h2>
                <span class="text-xs text-zinc-500 dark:text-zinc-400 mt-1.5 font-medium">系统底层的实时编译数据与核心依赖版本</span>
              </div>
            </div>

            <div class="flex items-center gap-3">
              <t-loading v-if="loading" size="small" />
              <t-tag v-if="buildInfo" theme="success" variant="light" shape="round" class="!px-3 !font-medium">
                <template #icon><check-circle-icon /></template>
                构建成功
              </t-tag>
            </div>
          </div>

          <template v-if="buildInfo">
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
              <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center transition-colors hover:bg-white dark:hover:bg-zinc-800">
                <div class="text-xs text-zinc-500 dark:text-zinc-400 mb-1.5 font-medium">当前版本</div>
                <div class="text-xl font-extrabold text-[var(--color-primary)] tracking-tight">{{ buildInfo.version }}</div>
              </div>
              <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center transition-colors hover:bg-white dark:hover:bg-zinc-800">
                <div class="text-xs text-zinc-500 dark:text-zinc-400 mb-1.5 font-medium">构建时间</div>
                <div class="text-sm font-bold text-zinc-800 dark:text-zinc-200 flex items-center gap-1.5">
                  <time-icon class="opacity-70 text-[var(--color-primary)]" /> {{ buildInfo.buildTime }}
                </div>
              </div>
              <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center transition-colors hover:bg-white dark:hover:bg-zinc-800" :title="buildInfo.commitMsg">
                <div class="text-xs text-zinc-500 dark:text-zinc-400 mb-1.5 font-medium">最新提交</div>
                <div class="text-sm font-mono font-bold text-zinc-800 dark:text-zinc-200 flex items-center gap-1.5">
                  <git-commit-icon class="opacity-70 text-[var(--color-primary)]" /> {{ buildInfo.commitId.substring(0, 7) }}
                </div>
                <div class="text-[11px] text-zinc-400 dark:text-zinc-500 mt-1 truncate font-medium">by {{ buildInfo.commitAuthor }}</div>
              </div>
              <div class="bg-zinc-50/80 dark:bg-zinc-800/50 p-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 flex flex-col justify-center transition-colors hover:bg-white dark:hover:bg-zinc-800">
                <div class="text-xs text-zinc-500 dark:text-zinc-400 mb-1.5 font-medium">核心框架</div>
                <div class="text-sm font-bold flex items-center gap-3">
                  <span class="text-[#512bd4] dark:text-[#a084fb]">.NET 10.0</span>
                  <span class="w-[1px] h-3 bg-zinc-300 dark:bg-zinc-600"></span>
                  <span class="text-[#42b883] dark:text-[#42b883]">Vue 3.x</span>
                </div>
              </div>
            </div>

            <div class="border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60 pt-4">
              <t-collapse :borderless="true">
                <t-collapse-panel value="history">
                  <template #header>
                    <div class="flex items-center gap-2 font-bold text-sm text-zinc-700 dark:text-zinc-300 bg-zinc-50/80 dark:bg-zinc-800/50 p-3 px-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors">
                      <history-icon class="opacity-80" /> 提交日志 (Commit History)
                    </div>
                  </template>
                  <div class="max-h-[300px] overflow-y-auto custom-scrollbar mt-3 pl-1 pr-2">
                    <t-timeline>
                      <t-timeline-item v-for="item in buildInfo.history" :key="item.commitId" dot-color="primary">
                        <div class="bg-zinc-50/50 dark:bg-zinc-800/30 p-3.5 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 ml-1 transition-colors hover:bg-white dark:hover:bg-zinc-800">
                          <div class="text-[11px] text-zinc-400 dark:text-zinc-500 font-mono mb-1.5 font-medium">{{ item.commitTime }}</div>
                          <div class="text-sm text-zinc-800 dark:text-zinc-200 font-medium mb-3 leading-snug">{{ item.commitMsg }}</div>
                          <div class="flex items-center gap-2">
                            <span class="inline-flex items-center gap-1 text-[11px] font-bold bg-zinc-200/50 dark:bg-zinc-700/50 text-zinc-600 dark:text-zinc-300 px-2 py-0.5 rounded">
                              <user-circle-icon size="12px" /> {{ item.commitAuthor }}
                            </span>
                            <span class="text-[11px] font-mono font-medium text-zinc-400 dark:text-zinc-500 bg-zinc-100 dark:bg-zinc-900 px-1.5 py-0.5 rounded border border-zinc-200/50 dark:border-zinc-700/50">#{{ item.commitId.substring(0, 7) }}</span>
                          </div>
                        </div>
                      </t-timeline-item>
                    </t-timeline>
                  </div>
                </t-collapse-panel>

                <t-collapse-panel value="dependencies">
                  <template #header>
                    <div class="flex items-center gap-2 font-bold text-sm text-zinc-700 dark:text-zinc-300 bg-zinc-50/80 dark:bg-zinc-800/50 p-3 px-4 rounded-xl border border-zinc-200/50 dark:border-zinc-700/50 hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors mt-2">
                      <code-icon class="opacity-80" /> 核心依赖 (Dependencies)
                    </div>
                  </template>
                  <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-3 mt-3">
                    <div v-for="dep in dependenciesList" :key="dep.name" class="flex items-center justify-between p-2.5 px-3.5 bg-zinc-50/80 dark:bg-zinc-800/50 border border-zinc-200/50 dark:border-zinc-700/50 rounded-lg shadow-sm transition-colors hover:bg-white dark:hover:bg-zinc-800 hover:border-[var(--color-primary)]/30">
                      <span class="text-xs font-bold text-zinc-700 dark:text-zinc-300 truncate mr-3" :title="dep.name">{{ dep.name }}</span>
                      <span class="text-[10px] font-mono font-bold px-1.5 py-0.5 rounded bg-zinc-200/50 dark:bg-zinc-700/50 text-zinc-500 dark:text-zinc-400 shrink-0">{{ dep.version }}</span>
                    </div>
                  </div>
                </t-collapse-panel>
              </t-collapse>
            </div>
          </template>
        </div>

      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

/* 首次渲染阶梯滑入动画 */
.list-item-anim {
  animation: slideUp 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.custom-scrollbar {
  .scrollbar-mixin();
}

:deep(.t-timeline-item__wrapper) {
  margin-left: 0 !important;
}
:deep(.t-timeline-item__label) {
  display: none !important;
}

</style>
