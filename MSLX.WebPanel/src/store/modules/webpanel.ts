import { defineStore } from 'pinia';
import {
  getWebpanelStyleSettings,
  updateWebpanelStyleSettings
} from '@/api/settings';
import { initUpload, uploadChunk, finishUpload } from '@/api/files';
import { uploadFilesToStaticImages } from '@/api/files';
import { MessagePlugin } from 'tdesign-vue-next';
import { WebpanelSettingsModel } from '@/api/model/settings';

export const useWebpanelStore = defineStore('webpanel', {
  state: () => ({
    settings: {
      webPanelStyleDarkBackgroundOpacity: 1.0,
      webPanelStyleDarkComponentsOpacity: 0.4,
      webPanelStyleLightBackground: '',
      webPanelStyleLightBackgroundOpacity: 1.0,
      webPanelStyleLightComponentsOpacity: 0.6,
      webPanelStyleNightBackground: '',
    } as WebpanelSettingsModel,
    loading: false,
    submitLoading: false,
  }),
  actions: {
    async fetchSettings() {
      this.loading = true;
      try {
        const res = await getWebpanelStyleSettings();
        this.settings = res;
      } catch (e: any) {
        console.error('获取面板样式失败', e);
      } finally {
        this.loading = false;
      }
    },
    async saveSettings() {
      this.submitLoading = true;
      try {
        await updateWebpanelStyleSettings(this.settings);
        MessagePlugin.success('面板样式保存成功');
      } catch (e: any) {
        MessagePlugin.error('保存失败: ' + e.message);
      } finally {
        this.submitLoading = false;
      }
    },
    // 修复后的上传逻辑
    async uploadImage(file: File): Promise<string | null> {
      // 增加安全检查
      if (!file) {
        MessagePlugin.error('文件对象无效');
        return null;
      }
      const isImage = ['image/png', 'image/jpeg', 'image/webp'].includes(file.type);
      if (!isImage) {
        MessagePlugin.error('仅支持 PNG, JPG, WEBP 格式的图片');
        return null;
      }

      const isLt10M = file.size / 1024 / 1024 < 10;
      if (!isLt10M) {
        MessagePlugin.error('图片大小不能超过 10MB');
        return null;
      }

      try {
        const { uploadId } = await initUpload();
        await uploadChunk(uploadId, 1, file);
        await finishUpload(uploadId, 1);
        await uploadFilesToStaticImages(uploadId, file.name);

        MessagePlugin.success(`上传成功: ${file.name}`);
        return file.name;
      } catch (e: any) {
        MessagePlugin.error('上传失败: ' + (e.message || '网络错误'));
        return null;
      }
    }
  },
  persist: {
    key: 'webpanel-style-storage',
    paths: ['settings'],
  },
});
