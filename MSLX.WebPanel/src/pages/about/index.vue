<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  CheckCircleIcon,
  CodeIcon,
  GitCommitIcon,
  HistoryIcon,
  ServerIcon,
  TimeIcon,
  UserCircleIcon,
} from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';

import type { BuildInfoModel } from '@/api/model/buildInfo';
import { getBuildInfo } from '@/api/buildInfo';

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

const loading = ref(true);
const buildInfo = ref<BuildInfoModel | null>(null);

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

const dependenciesList = computed(() => {
  if (!buildInfo.value?.dependencies) return [];
  return Object.entries(buildInfo.value.dependencies).map(([k, v]) => ({ name: k, version: v }));
});

onMounted(() => {
  fetchBuildInfo();
});
</script>

<template>
  <div class="about-page">
    <t-space direction="vertical" size="large" style="width: 100%">
      <t-card :bordered="false" class="intro-card">
        <div class="intro-header">
          <div class="logo-area">
            <server-icon class="logo-icon" />
            <h1 class="project-title">关于 MSLX</h1>
          </div>
        </div>

        <div class="intro-text">
          <p>
            <strong>MSLX</strong> 是由 <strong>MSL 原班团队 MSLTeam</strong> 倾力打造的全新一代开服工具。 基于
            <strong>.NET Core 10.0</strong> 环境。
          </p>
          <p>
            它传承了 MSL 经典的 UI 设计语言，旨在让操作零门槛——无论是老用户还是新伙伴，都能即刻上手，极速部署您的 MC
            服务器。 MSLX 不仅 <strong>完美支持跨平台</strong> (Windows / macOS / Linux) 运行，相比前代，更引入了强大的
            <strong>远程访问</strong> 功能，让管理更自由。
          </p>
        </div>
      </t-card>

      <t-card :bordered="false" title="开发团队">
        <p class="about-desc">感谢以下开发者对本项目的杰出贡献：</p>
        <t-row :gutter="[16, 16]">
          <t-col v-for="dev in developers" :key="dev.name" :span="3" :xs="12" :sm="6">
            <div class="dev-card">
              <t-avatar :image="dev.avatar" size="56px" shape="circle" class="dev-avatar" />
              <div class="dev-info">
                <div class="dev-name">{{ dev.name }}</div>
                <div class="dev-role">{{ dev.role }}</div>
                <div class="dev-desc" :title="dev.desc">{{ dev.desc }}</div>
              </div>
            </div>
          </t-col>
        </t-row>
      </t-card>

      <t-card :bordered="false" title="构建信息" :loading="loading">
        <template #actions>
          <t-tag v-if="buildInfo" theme="success" variant="light">
            <template #icon><check-circle-icon /></template>
            构建成功
          </t-tag>
        </template>

        <div v-if="buildInfo" class="build-summary">
          <t-row :gutter="[16, 16]" style="align-items: stretch">
            <t-col :span="3" :xs="12" :sm="6">
              <div class="info-block">
                <div class="label">当前版本</div>
                <div class="value version">{{ buildInfo.version }}</div>
              </div>
            </t-col>
            <t-col :span="3" :xs="12" :sm="6">
              <div class="info-block">
                <div class="label">构建时间</div>
                <div class="value time"><time-icon /> {{ buildInfo.buildTime }}</div>
              </div>
            </t-col>
            <t-col :span="3" :xs="12" :sm="6">
              <div class="info-block">
                <div class="label">最新提交</div>
                <div class="value commit" :title="buildInfo.commitMsg">
                  <git-commit-icon /> {{ buildInfo.commitId.substring(0, 7) }}
                </div>
                <div class="sub-value">by {{ buildInfo.commitAuthor }}</div>
              </div>
            </t-col>
            <t-col :span="3" :xs="12" :sm="6">
              <div class="info-block">
                <div class="label">核心框架</div>
                <div class="value framework">
                  <span class="dotnet">.NET 10.0</span>
                  <t-divider layout="vertical" />
                  <span class="vue">Vue 3.x</span>
                </div>
              </div>
            </t-col>
          </t-row>
        </div>

        <div v-if="buildInfo" class="details-collapse">
          <t-collapse :borderless="true">
            <t-collapse-panel value="history">
              <template #header>
                <div class="panel-header"><history-icon /> 更新日志 (Commit History)</div>
              </template>

              <div class="history-timeline">
                <t-timeline>
                  <t-timeline-item v-for="item in buildInfo.history" :key="item.commitId" dot-color="primary">
                    <div class="timeline-content">
                      <div class="commit-time">{{ item.commitTime }}</div>

                      <div class="msg">{{ item.commitMsg }}</div>

                      <div class="meta">
                        <t-tag size="small" style="padding: 0">
                          <user-circle-icon /> {{ item.commitAuthor }}
                        </t-tag>
                        <span class="hash">#{{ item.commitId.substring(0, 7) }}</span>
                      </div>
                    </div>
                  </t-timeline-item>
                </t-timeline>
              </div>
            </t-collapse-panel>

            <t-collapse-panel value="dependencies">
              <template #header>
                <div class="panel-header"><code-icon /> 核心依赖 (Dependencies)</div>
              </template>
              <div class="deps-grid">
                <t-row :gutter="[12, 12]">
                  <t-col v-for="dep in dependenciesList" :key="dep.name" :span="3" :xs="12" :sm="6" :md="4">
                    <div class="dep-item">
                      <span class="dep-name" :title="dep.name">{{ dep.name }}</span>
                      <t-tag size="small" variant="light-outline">{{ dep.version }}</t-tag>
                    </div>
                  </t-col>
                </t-row>
              </div>
            </t-collapse-panel>
          </t-collapse>
        </div>
      </t-card>
    </t-space>
  </div>
</template>

<style scoped lang="less">
.about-page {
  margin: 0 auto;
  padding-bottom: 24px;
}

.intro-card {
  .intro-header {
    display: flex;
    align-items: center;
    margin-bottom: 20px;
    .logo-area {
      display: flex;
      align-items: center;
      gap: 12px;
      .logo-icon {
        font-size: 36px;
        color: var(--td-brand-color);
      }
      .project-title {
        font-size: 24px;
        font-weight: 700;
        margin: 0;
        line-height: 1;
        color: var(--td-text-color-primary);
      }
    }
  }
  .intro-text {
    font-size: 14px;
    line-height: 1.8;
    color: var(--td-text-color-primary);
    background: var(--td-bg-color-secondarycontainer);
    padding: 20px;
    border-radius: var(--td-radius-medium);
    border-left: 4px solid var(--td-brand-color);
    p {
      margin-bottom: 12px;
      &:last-child {
        margin-bottom: 0;
      }
    }
    strong {
      color: var(--td-brand-color);
      font-weight: 600;
    }
  }
}

.build-summary {
  margin-bottom: 24px;
  .info-block {
    background: var(--td-bg-color-container-hover);
    padding: 16px;
    border-radius: var(--td-radius-medium);
    height: 100%;
    display: flex;
    flex-direction: column;
    justify-content: center;
    .label {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      margin-bottom: 8px;
    }
    .value {
      font-size: 16px;
      font-weight: 600;
      color: var(--td-text-color-primary);
      display: flex;
      align-items: center;
      gap: 6px;
      word-break: break-all;
      flex-wrap: wrap;
      &.version {
        font-size: 20px;
        color: var(--td-brand-color);
      }
      &.time {
        font-size: 15px;
      }
      &.framework {
        .dotnet {
          color: #512bd4;
        }
        .vue {
          color: #42b883;
        }
      }
    }
    .sub-value {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
    }
  }
}

.details-collapse {
  margin-top: 16px;
  border-top: 1px solid var(--td-component-stroke);
  :deep(.t-collapse-panel__header) {
    background-color: transparent;
  }
  .panel-header {
    display: flex;
    align-items: center;
    gap: 8px;
    font-weight: 500;
  }
}

.history-timeline {
  padding: 12px 0;
  max-height: 400px;
  overflow-y: auto;

  // 隐藏左边留空
  :deep(.t-timeline-item__wrapper) {
    margin-left: 0 !important;
  }

  :deep(.t-timeline-item__label) {
    display: none !important;
  }

  .timeline-content {
    background: var(--td-bg-color-secondarycontainer);
    padding: 10px 14px;
    border-radius: 6px;

    .commit-time {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-bottom: 4px;
      font-family: monospace;
    }

    .msg {
      font-size: 14px;
      color: var(--td-text-color-primary);
      margin-bottom: 8px;
      word-break: break-word;
      line-height: 1.5;
      font-weight: 500;
    }
    .meta {
      display: flex;
      align-items: center;
      gap: 8px;
      .hash {
        font-family: monospace;
        color: var(--td-text-color-placeholder);
        font-size: 12px;
      }
    }
  }
}

.deps-grid {
  .dep-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 8px 12px;
    background: var(--td-bg-color-component);
    border: 1px solid var(--td-component-border);
    border-radius: 4px;
    .dep-name {
      font-size: 13px;
      color: var(--td-text-color-primary);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      margin-right: 8px;
      flex: 1;
    }
  }
}

.about-desc {
  color: var(--td-text-color-secondary);
  margin-bottom: 16px;
  font-size: 14px;
}
.dev-card {
  display: flex;
  align-items: center;
  background-color: var(--td-bg-color-container-hover);
  padding: 16px;
  border-radius: var(--td-radius-medium);
  transition: all 0.3s cubic-bezier(0.38, 0, 0.24, 1);
  border: 1px solid transparent;

  &:hover {
    transform: translateY(-4px);
    box-shadow: var(--td-shadow-2);
    border-color: var(--td-brand-color-light);
    background-color: var(--td-bg-color-container);
  }
  .dev-avatar {
    flex-shrink: 0;
    border: 2px solid var(--td-bg-color-container);
    box-shadow: var(--td-shadow-1);
  }
  .dev-info {
    margin-left: 16px;
    overflow: hidden;
    flex: 1;
    .dev-name {
      font-weight: 700;
      font-size: 16px;
      color: var(--td-text-color-primary);
      line-height: 1.4;
    }
    .dev-role {
      font-size: 12px;
      color: var(--td-brand-color);
      font-weight: 500;
      background: var(--td-brand-color-light-hover);
      display: inline-block;
      padding: 2px 6px;
      border-radius: 4px;
      margin: 4px 0;
    }
    .dev-desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
  }
}

@media screen and (max-width: 768px) {
  .intro-card .intro-header {
    flex-direction: column;
    align-items: flex-start;
  }
  .intro-text {
    padding: 16px;
    font-size: 13px;
  }
  .build-summary .info-block {
    height: auto;
    min-height: 80px;
  }
  .dev-card {
    padding: 12px;
    .dev-avatar {
      width: 48px;
      height: 48px;
    }
    .dev-info {
      margin-left: 12px;
    }
  }
}
</style>
