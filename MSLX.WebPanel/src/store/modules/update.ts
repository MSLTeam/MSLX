// src/store/update.ts
import { defineStore } from 'pinia';
import { ref } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { getDaemonUpdateInfo, getDaemonUpdateDownloadInfo } from '@/api/update';
import type { UpdateInfoModel, UpdateDownloadInfoModel } from '@/api/model/update';

export const useUpdateStore = defineStore('update', () => {
  // === State ===
  const showUpdateModal = ref(false);
  const updateInfo = ref<UpdateInfoModel | null>(null);
  const downloadInfo = ref<UpdateDownloadInfoModel | null>(null);
  const loading = ref(false); // 检查更新时的 loading 状态

  // === Actions ===

  /**
   * 检查更新
   * @param force 是否强制检查（忽略跳过的版本）
   */
  const checkAppUpdate = async (force = false) => {
    if (loading.value) return;
    loading.value = true;

    try {
      // 获取版本信息
      const res = await getDaemonUpdateInfo();
      const data = res as unknown as UpdateInfoModel;

      if (data && data.needUpdate) {
        // 检查是否跳过
        if (!force) {
          const skippedVersion = localStorage.getItem('mslx-skip-version');
          if (skippedVersion === data.latestVersion) {
            console.log(`[Update] 用户已跳过版本 ${data.latestVersion}`);
            return;
          }
        }

        updateInfo.value = data;

        // 获取下载链接
        try {
          const dlRes = await getDaemonUpdateDownloadInfo();
          downloadInfo.value = dlRes as unknown as UpdateDownloadInfoModel;
        } catch (e) {
          console.error('获取下载链接失败', e);
          downloadInfo.value = { web: '', file: '' };
        }

        // 弹窗
        showUpdateModal.value = true;

        if (force) {
          MessagePlugin.success('发现新版本！');
        }
      } else {
        if (force) {
          MessagePlugin.success('当前已是最新版本');
        }
      }
    } catch (error) {
      console.error('[Update] 检查更新失败:', error);
      if (force) {
        MessagePlugin.error('检查更新失败，请检查网络日志');
      }
    } finally {
      loading.value = false;
    }
  };

  // 处理“跳过此版本”
  const handleSkipVersion = () => {
    if (updateInfo.value?.latestVersion) {
      localStorage.setItem('mslx-skip-version', updateInfo.value.latestVersion);
      MessagePlugin.success('已跳过该版本，下次将不再提醒');
      showUpdateModal.value = false;
    }
  };

  return {
    showUpdateModal,
    updateInfo,
    downloadInfo,
    loading,
    checkAppUpdate,
    handleSkipVersion,
  };
});
