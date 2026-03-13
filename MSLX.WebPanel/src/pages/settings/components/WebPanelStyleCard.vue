<script setup lang="ts">
import {
  BookIcon,
  QuestionnaireDoubleFilledIcon,
  UploadIcon,
} from 'tdesign-icons-vue-next';
import { useWebpanelStore } from '@/store/modules/webpanel';
import { changeUrl } from '@/router';
import { DOC_URLS } from '@/api/docs';

const webpanelStore = useWebpanelStore();

// 上传背景
const handleFileUpload = async (
  files: any,
  targetField: 'webPanelStyleLightBackground' | 'webPanelStyleDarkBackground',
) => {
  const rawFile = files[0]?.raw || files.raw;
  if (!rawFile) return;

  const fileName = await webpanelStore.uploadImage(rawFile);
  if (fileName) {
    webpanelStore.settings[targetField] = fileName;
  }
};
</script>

<template>
  <div class="design-card list-item-anim relative flex flex-col bg-white/80 dark:bg-zinc-800/80 backdrop-blur-md rounded-2xl border border-zinc-200/50 dark:border-zinc-700/50 shadow-sm transition-all duration-300">

    <t-loading :loading="webpanelStore.loading" show-overlay>
      <div class="p-5 sm:p-6 sm:px-8">

        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <div class="flex items-center gap-3">
            <div class="w-1.5 h-6 bg-[var(--color-primary)] rounded-full shadow-[0_0_8px_var(--color-primary-light)] opacity-90"></div>
            <div class="flex flex-col">
              <h2 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none tracking-tight">面板自定义样式</h2>
              <span class="text-[11px] sm:text-xs text-amber-600/80 dark:text-amber-500/80 mt-1.5 font-medium">背景图相关的样式需要在面板左上角的样式面板中启用背景美化才会生效哦！</span>
            </div>
          </div>
          <t-button variant="dashed" size="small" class="!bg-transparent" @click="changeUrl(DOC_URLS.style)">
            <template #icon><book-icon /></template>
            设置文档
          </t-button>
        </div>

        <t-form :data="webpanelStore.settings" label-align="top" @submit="webpanelStore.saveSettings">

          <div class="flex items-center gap-3 mt-2 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">背景图片设置</span>
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <t-form-item label="浅色背景">
            <template #help><span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">留空则使用默认的背景图哦～</span></template>
            <t-input
              v-model="webpanelStore.settings.webPanelStyleLightBackground"
              placeholder="输入完整 URL 地址或者在右边上传图片"
              class="!w-full"
            >
              <template #suffix>
                <t-upload
                  theme="custom"
                  action=""
                  :auto-upload="false"
                  :show-file-list="false"
                  accept="image/png, image/jpeg, image/webp"
                  @change="(val) => handleFileUpload(val, 'webPanelStyleLightBackground')"
                >
                  <t-button variant="text" shape="square" class="hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)] !h-auto !w-auto !p-1.5 !rounded-md transition-colors" title="上传本地图片">
                    <upload-icon />
                  </t-button>
                </t-upload>
              </template>
            </t-input>
          </t-form-item>

          <t-form-item label="深色背景">
            <t-input
              v-model="webpanelStore.settings.webPanelStyleDarkBackground"
              placeholder="输入完整 URL 地址或者在右边上传图片"
              class="!w-full"
            >
              <template #suffix>
                <t-upload
                  theme="custom"
                  :auto-upload="false"
                  :show-file-list="false"
                  accept="image/png, image/jpeg, image/webp"
                  @change="(val) => handleFileUpload(val, 'webPanelStyleDarkBackground')"
                >
                  <t-button variant="text" shape="square" class="hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)] !h-auto !w-auto !p-1.5 !rounded-md transition-colors" title="上传本地图片">
                    <upload-icon />
                  </t-button>
                </t-upload>
              </template>
            </t-input>
          </t-form-item>

          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">透明度调整 (0.1 - 1.0)</span>
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-x-10 gap-y-2">
            <t-form-item label="浅色背景透明度">
              <t-slider
                v-model="webpanelStore.settings.webPanelStyleLightBackgroundOpacity"
                :min="0.1" :max="1" :step="0.01"
                :tooltip-props="{ theme: 'light' }"
              />
            </t-form-item>
            <t-form-item label="浅色组件透明度">
              <t-slider
                v-model="webpanelStore.settings.webPanelStyleLightComponentsOpacity"
                :min="0.1" :max="1" :step="0.01"
                :tooltip-props="{ theme: 'light' }"
              />
            </t-form-item>
            <t-form-item label="深色背景透明度">
              <t-slider
                v-model="webpanelStore.settings.webPanelStyleDarkBackgroundOpacity"
                :min="0.1" :max="1" :step="0.01"
              />
            </t-form-item>
            <t-form-item label="深色组件透明度">
              <t-slider
                v-model="webpanelStore.settings.webPanelStyleDarkComponentsOpacity"
                :min="0.1" :max="1" :step="0.01"
              />
            </t-form-item>
          </div>

          <div class="flex items-center gap-3 mt-8 mb-6">
            <span class="text-xs font-extrabold text-[var(--td-text-color-secondary)] uppercase tracking-widest">终端设置 (毛玻璃强度/染色设置)</span>
            <div class="h-px bg-zinc-200/60 dark:bg-zinc-700/60 flex-1"></div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-x-10 gap-y-4">
            <t-form-item label="浅色模式模糊度">
              <t-slider
                v-model="webpanelStore.settings.webpPanelTerminalBlurLight"
                :min="0" :max="50" :step="1"
                :input-number-props="{ theme: 'column', style: 'width: 65px' } as any"
              >
                <template #label="{ value }"> {{ value }}px </template>
              </t-slider>
            </t-form-item>

            <t-form-item label="深色模式模糊度">
              <t-slider
                v-model="webpanelStore.settings.webpPanelTerminalBlurDark"
                :min="0" :max="50" :step="1"
                :input-number-props="{ theme: 'column', style: 'width: 65px' } as any"
              >
                <template #label="{ value }"> {{ value }}px </template>
              </t-slider>
            </t-form-item>

            <t-form-item label="日志染色等级">
              <div class="flex items-center gap-3 w-full">
                <t-select v-model="webpanelStore.settings.webPanelColorizeLogLevel" class="!flex-1 sm:!flex-none sm:!w-48">
                  <t-option label="不染色" :value="0" />
                  <t-option label="简约染色" :value="1" />
                  <t-option label="增强染色" :value="2" />
                </t-select>
                <t-button theme="default" class="!bg-zinc-100 dark:!bg-zinc-800/80 !text-zinc-600 dark:!text-zinc-400 !border-none hover:!bg-[var(--color-primary)]/10 hover:!text-[var(--color-primary)]" @click="changeUrl(DOC_URLS.style_log_colorizer)">
                  <template #icon>
                    <questionnaire-double-filled-icon />
                  </template>
                  有什么区别？
                </t-button>
              </div>
            </t-form-item>
          </div>

          <div class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60">
            <t-button theme="primary" type="submit" :loading="webpanelStore.submitLoading" class="!h-10 !w-full sm:!w-auto sm:!px-10 !font-bold tracking-widest !rounded-xl shadow-md shadow-[var(--color-primary-light)]/40 hover:shadow-[var(--color-primary-light)]/60 transition-shadow">
              应用样式设置
            </t-button>
          </div>

        </t-form>
      </div>
    </t-loading>
  </div>
</template>

<style scoped>
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


:deep(.t-input__suffix) {
  @apply !flex !items-center;
}
:deep(.t-upload) {
  @apply !w-auto !inline-flex !align-middle;
}
:deep(.t-upload__content) {
  @apply !flex;
}
:deep(.t-upload__tips) {
  @apply !hidden;
}
</style>
