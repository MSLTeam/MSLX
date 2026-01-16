<template>
  <router-view :class="[mode]" />
  <update-modal
    :visible="showUpdateModal"
    :update-info="updateInfo"
    :download-info="downloadInfo"
    @close="showUpdateModal = false"
    @skip="handleSkipVersion"
  />
</template>
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useSettingStore } from '@/store';
import { UpdateDownloadInfoModel, UpdateInfoModel } from '@/api/model/update';
import { getDaemonUpdateDownloadInfo, getDaemonUpdateInfo } from '@/api/update';
import { MessagePlugin } from 'tdesign-vue-next';
import UpdateModal from '@/components/UpdateModal.vue';

const store = useSettingStore();

const mode = computed(() => {
  return store.displayMode;
});

// 检查后端更新的状态管理
const showUpdateModal = ref(false);
const updateInfo = ref<UpdateInfoModel | null>(null);
const downloadInfo = ref<UpdateDownloadInfoModel | null>(null);
// 检查更新的主逻辑
const checkAppUpdate = async () => {
  try {
    // 获取版本信息
    const res = await getDaemonUpdateInfo();
    const data = res as unknown as UpdateInfoModel;

    if (data && data.needUpdate) {
      // 用户是否跳过了该版本
      const skippedVersion = localStorage.getItem('mslx-skip-version');
      if (skippedVersion === data.latestVersion) {
        console.log(`[Update] 用户已跳过版本 ${data.latestVersion}`);
        return;
      }

      // 获取下载链接
      updateInfo.value = data;
      try {
        const dlRes = await getDaemonUpdateDownloadInfo();
        downloadInfo.value = dlRes as unknown as UpdateDownloadInfoModel;
      } catch (e) {
        console.error('获取下载链接失败', e);
        downloadInfo.value = { web: '', file: '' };
      }

      // 显示弹窗
      showUpdateModal.value = true;
    }
  } catch (error) {
    console.error('[Update] 检查更新失败:', error);
  }
};

// 处理“跳过此版本”
const handleSkipVersion = () => {
  if (updateInfo.value?.latestVersion) {
    localStorage.setItem('mslx_skip_version', updateInfo.value.latestVersion);
    MessagePlugin.success('已跳过该版本，下次将不再提醒');
    showUpdateModal.value = false;
  }
};

onMounted(() => {
  checkAppUpdate();
});
</script>
<style lang="less" scoped>
#nprogress .bar {
  background: var(--td-brand-color) !important;
}
</style>
