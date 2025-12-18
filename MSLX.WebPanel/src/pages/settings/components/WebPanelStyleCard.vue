<script setup lang="ts">
import { UploadIcon } from 'tdesign-icons-vue-next';
import { useWebpanelStore } from '@/store/modules/webpanel';

const webpanelStore = useWebpanelStore();

// 上传背景
const handleFileUpload = async (files: any, targetField: 'webPanelStyleLightBackground' | 'webPanelStyleNightBackground') => {
  const rawFile = files[0]?.raw || files.raw;
  if (!rawFile) return;

  const fileName = await webpanelStore.uploadImage(rawFile);
  if (fileName) {
    webpanelStore.settings[targetField] = fileName;
  }
};
</script>

<template>
  <t-card :bordered="false" title="面板自定义样式" description="这里设置的样式需要在面板左上角的样式面板中启用背景美化才会生效哦！" :loading="webpanelStore.loading" class="settings-card">
    <t-form :data="webpanelStore.settings" :label-width="120" label-align="left" @submit="webpanelStore.saveSettings">

      <div class="group-title">背景图片设置</div>
      <t-form-item label="浅色背景" help="留空则使用默认的背景图哦～">
        <t-input v-model="webpanelStore.settings.webPanelStyleLightBackground" placeholder="输入完整URL地址或者在右边上传图片">
          <template #suffix>
            <t-upload
              theme="custom"
              action=""
              :auto-upload="false"
              :show-file-list="false"
              accept="image/png, image/jpeg, image/webp"
              @change="(val) => handleFileUpload(val, 'webPanelStyleLightBackground')"
            >
              <t-button variant="text" shape="square"><upload-icon /></t-button>
            </t-upload>
          </template>
        </t-input>
      </t-form-item>

      <t-form-item label="深色背景">
        <t-input v-model="webpanelStore.settings.webPanelStyleNightBackground" placeholder="输入完整URL地址或者在右边上传图片">
          <template #suffix>
            <t-upload
              theme="custom"
              :auto-upload="false"
              :show-file-list="false"
              accept="image/png, image/jpeg, image/webp"
              @change="(val) => handleFileUpload(val, 'webPanelStyleNightBackground')"
            >
              <t-button variant="text" shape="square"><upload-icon /></t-button>
            </t-upload>
          </template>
        </t-input>
      </t-form-item>

      <t-divider dashed />

      <div class="group-title">透明度调整 (0.1 - 1.0)</div>
      <t-row :gutter="[32, 16]">
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="浅色背景透明度">
            <t-slider v-model="webpanelStore.settings.webPanelStyleLightBackgroundOpacity" :min="0.1" :max="1" :step="0.01" :tooltip-props="{ theme: 'light' }" />
          </t-form-item>
        </t-col>
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="浅色组件透明度">
            <t-slider v-model="webpanelStore.settings.webPanelStyleLightComponentsOpacity" :min="0.1" :max="1" :step="0.01" :tooltip-props="{ theme: 'light' }" />
          </t-form-item>
        </t-col>
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="深色背景透明度">
            <t-slider v-model="webpanelStore.settings.webPanelStyleDarkBackgroundOpacity" :min="0.1" :max="1" :step="0.01" />
          </t-form-item>
        </t-col>
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="深色组件透明度">
            <t-slider v-model="webpanelStore.settings.webPanelStyleDarkComponentsOpacity" :min="0.1" :max="1" :step="0.01" />
          </t-form-item>
        </t-col>
      </t-row>

      <t-divider dashed />

      <div class="group-title">终端毛玻璃强度 (Blur)</div>
      <t-row :gutter="[32, 16]">
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="浅色模式模糊度">
            <t-slider
              v-model="webpanelStore.settings.webpPanelTerminalBlurLight"
              :min="0"
              :max="50"
              :step="1"
              :input-number-props="{ theme: 'column', style: 'width: 65px' } as any"
            >
              <template #label="{ value }">
                {{ value }}px
              </template>
            </t-slider>
          </t-form-item>
        </t-col>
        <t-col :xs="24" :sm="12" :md="6">
          <t-form-item label="深色模式模糊度">
            <t-slider
              v-model="webpanelStore.settings.webpPanelTerminalBlurDark"
              :min="0"
              :max="50"
              :step="1"
              :input-number-props="{ theme: 'column', style: 'width: 65px' } as any"
            >
              <template #label="{ value }">
                {{ value }}px
              </template>
            </t-slider>
          </t-form-item>
        </t-col>
      </t-row>

      <t-form-item style="margin-top: 24px">
        <t-button theme="primary" type="submit" :loading="webpanelStore.submitLoading" block class="action-btn">
          应用样式设置
        </t-button>
      </t-form-item>
    </t-form>
  </t-card>
</template>

<style scoped lang="less">
.settings-card {
  border-radius: 8px;
  overflow: hidden;
  transition: all 0.3s;
  &:hover { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05); }
}

.group-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--td-text-color-placeholder);
  margin: 8px 0 16px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.action-btn {
  margin-top: 12px;
  font-weight: 600;
  height: 40px;
}

:deep(.t-input__suffix) {
  display: flex;
  align-items: center;

  .t-upload {
    width: auto;
    display: inline-flex;
    vertical-align: middle;
  }
  .t-upload__content { display: flex; }
  .t-upload__tips { display: none; }
}

:deep(.t-upload__single) {
  display: flex;
  align-items: center;
}

/* 移动端适配：将表单项改为垂直排列，防止滑块被挤成点 */
@media screen and (max-width: 768px) {
  :deep(.t-form-item) {
    flex-direction: column;
    align-items: flex-start;
  }
  :deep(.t-form__label) {
    width: 100% !important;
    text-align: left;
    margin-right: 0 !important;
    margin-bottom: 8px;
  }
  :deep(.t-form__controls) {
    margin-left: 0 !important;
    width: 100%;

    .t-slider {
      width: 100%;
    }
  }
}
</style>
