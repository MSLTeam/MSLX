<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import QuickMode from './components/QuickMode.vue';
import CustomMode from './components/CustomMode.vue';
import PackageMode from './components/PackageMode.vue';
import McdrMode from './components/McdrMode.vue';
import BedrockMode from '@/pages/instance/createInstance/components/BedrockMode.vue';
import NodeSwitcher from '@/components/node-switcher/index.vue';

import { useNodeStore } from '@/store';
import { useRoute } from 'vue-router';
import { getPageNodeId } from '@/utils/nodeTarget';
import { getUploadResumeRecords } from '@/utils/uploadResume';
import {
  clearCreateInstanceUploads,
  CREATE_DRAFT_TTL_MS,
  readCreateInstanceDraft,
  removeCreateInstanceDraft,
  type CreateInstanceDraftMode,
} from './createInstanceDraft';

const route = useRoute();
const value = ref(route.query.mode ? Number(route.query.mode) : 1);
const renderKey = ref(0);
const restoringMode = ref(true);

const modeByNumber: Record<number, CreateInstanceDraftMode> = {
  1: 'quick',
  2: 'package',
  3: 'bedrock',
  4: 'mcdr',
  10: 'custom',
};

const getModeStorageKey = () => `mslx-create-mode:${getPageNodeId()}`;

const readSavedMode = (): number | null => {
  try {
    const raw = localStorage.getItem(getModeStorageKey());
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { mode?: number; updatedAt?: number };
    if (typeof parsed === 'object' && parsed !== null && typeof parsed.mode === 'number') {
      if (typeof parsed.updatedAt !== 'number' || Date.now() - parsed.updatedAt > CREATE_DRAFT_TTL_MS) {
        localStorage.removeItem(getModeStorageKey());
        return null;
      }
      return parsed.mode;
    }
    const legacyMode = Number(raw);
    return modeByNumber[legacyMode] ? legacyMode : null;
  } catch {
    return null;
  }
};

const saveCurrentMode = (mode: number) => {
  try {
    localStorage.setItem(getModeStorageKey(), JSON.stringify({ mode, updatedAt: Date.now() }));
  } catch {
    // 忽略本地存储不可用的情况。
  }
};

watch(() => route.query.mode, (newMode) => {
  if (newMode) {
    value.value = Number(newMode);
  }
});

const nodeStore = useNodeStore();

const restorePendingPackageMode = async () => {
  // 路由显式指定模式时，以路由为准。
  if (route.query.mode) return;

  let hasSavedMode = false;
  hasSavedMode = readSavedMode() !== null;

  let latestDraft: { mode: CreateInstanceDraftMode; updatedAt: number } | null = null;
  for (const mode of Object.values(modeByNumber)) {
    const draft = readCreateInstanceDraft(mode);
    if (draft && (!latestDraft || draft.updatedAt > latestDraft.updatedAt)) {
      latestDraft = { mode, updatedAt: draft.updatedAt };
    }
  }
  if (!hasSavedMode && latestDraft) {
    const draftMode = Object.entries(modeByNumber).find(([, mode]) => mode === latestDraft?.mode)?.[0];
    if (draftMode) value.value = Number(draftMode);
  }

  const records = await getUploadResumeRecords(getPageNodeId()).catch(() => []);
  const hasPendingPackageUpload = records.some((record) => {
    if (record.cancelledAt) return false;
    if (record.kind) return record.kind === 'package';
    return /\.(?:zip|mrpack)$/i.test(record.fileName);
  });
  if (!hasSavedMode && hasPendingPackageUpload) value.value = 2;
};

watch(value, (mode) => {
  if (restoringMode.value || !modeByNumber[mode]) return;
  saveCurrentMode(mode);
});

const restartCreation = async () => {
  const mode = modeByNumber[value.value];
  if (!mode) return;

  removeCreateInstanceDraft(mode);
  const uploadKind = mode === 'package' ? 'package' : mode === 'quick' || mode === 'mcdr' ? 'core' : undefined;
  await clearCreateInstanceUploads(getPageNodeId(), uploadKind);
  renderKey.value += 1;
};

onMounted(async () => {
  if (!route.query.mode) {
    const savedMode = readSavedMode();
    if (savedMode !== null) value.value = savedMode;
  }
  await restorePendingPackageMode();
  restoringMode.value = false;
  if (modeByNumber[value.value]) saveCurrentMode(value.value);
});

const handleNodeChange = () => {};
</script>
<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">
    <div
      class="design-card list-item-anim flex flex-col sm:flex-row sm:items-center justify-between gap-5 p-5 sm:p-6 bg-[var(--td-bg-color-container)]/80 rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left"
      style="animation-delay: 0s"
    >
      <div class="flex items-center gap-3">
        <div class="flex flex-col">
          <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">
            创建服务端实例
          </h2>
          <span class="text-xs text-[var(--td-text-color-secondary)] mt-1.5 font-medium"
            >选择适合您的部署模式，快速建立或深度定制您的 Minecraft 服务器</span
          >
        </div>
      </div>

      <div class="flex items-center shrink-0 gap-3">
        <node-switcher @change="handleNodeChange" />
        <t-button theme="danger" variant="outline" size="small" @click="restartCreation">重新创建</t-button>
        <t-radio-group v-model="value" variant="default-filled">
          <t-radio-button :value="1">快速模式</t-radio-button>
          <t-radio-button :value="2">上传整合包</t-radio-button>
          <t-radio-button :value="3">基岩版</t-radio-button>
          <t-radio-button :value="4">MCDR</t-radio-button>
          <t-radio-button :value="10">自定义模式</t-radio-button>
        </t-radio-group>
      </div>
    </div>

    <div :key="`${nodeStore.activeNodeId}-${renderKey}`" class="relative w-full">
      <div v-if="value === 1" class="list-item-anim" style="animation-delay: 0.1s">
        <quick-mode />
      </div>

      <div v-if="value === 2" class="list-item-anim" style="animation-delay: 0.1s">
        <package-mode />
      </div>

      <div v-if="value === 3" class="list-item-anim" style="animation-delay: 0.1s">
        <bedrock-mode />
      </div>

      <div v-if="value === 4" class="list-item-anim" style="animation-delay: 0.1s">
        <mcdr-mode />
      </div>

      <div v-if="value === 10" class="list-item-anim" style="animation-delay: 0.1s">
        <custom-mode />
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 首次渲染阶梯滑入动画 */
.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
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
</style>
