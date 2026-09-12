<script setup lang="ts">
import { ref, watch, nextTick, onBeforeUnmount } from 'vue';
import Artplayer from 'artplayer';
import { DownloadIcon } from 'tdesign-icons-vue-next';

const props = defineProps<{
  visible: boolean;
  fileName: string;
  videoUrl: string;
}>();

const emit = defineEmits(['update:visible']);

const artRef = ref<HTMLDivElement | null>(null);
const playError = ref(false);
const errorMessage = ref('');
let artInstance: Artplayer | null = null;

const handleClose = () => {
  destroyPlayer();
  emit('update:visible', false);
};

const destroyPlayer = () => {
  if (artInstance) {
    try {
      artInstance.destroy(true);
    } catch (e) {
      console.warn('Destroy Artplayer error:', e);
    }
    artInstance = null;
  }
};

const initPlayer = () => {
  destroyPlayer();
  playError.value = false;
  errorMessage.value = '';

  if (!artRef.value || !props.videoUrl) return;

  artInstance = new Artplayer({
    container: artRef.value,
    url: props.videoUrl,
    volume: 0.7,
    isLive: false,
    muted: false,
    autoplay: true,
    pip: true,
    autoSize: false,
    screenshot: true,
    setting: true,
    loop: false,
    flip: true,
    playbackRate: true,
    aspectRatio: true,
    fullscreen: true,
    fullscreenWeb: true,
    miniProgressBar: true,
    mutex: true,
    backdrop: true,
    playsInline: true,
    theme: '#0052d9',
    lang: 'zh-cn',
    hotkey: true,
    fastForward: true,
    lock: true,
    icons: {
      loading: '<div class="art-loading-indicator">加载中...</div>',
    },
  });

  artInstance.on('error', (error) => {
    console.error('Artplayer playback error:', error);
    playError.value = true;
    errorMessage.value = '视频无法在此浏览器中直接播放（可能是格式或编码不兼容，如 H.265/HEVC、AVI 或特定 MKV 编码），建议下载到本地使用专用播放器播放。';
  });
};

watch(
  () => [props.visible, props.videoUrl],
  ([visible, url]) => {
    if (visible && url) {
      nextTick(() => {
        initPlayer();
      });
    } else {
      destroyPlayer();
    }
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  destroyPlayer();
});
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="fileName || '视频预览'"
    :footer="false"
    :draggable="true"
    width="850px"
    top="8vh"
    class="video-preview-dialog"
    @close="handleClose"
  >
    <div class="relative w-full bg-black min-h-[380px] aspect-video flex items-center justify-center overflow-hidden rounded-b-md">
      <div v-show="!playError" ref="artRef" class="w-full h-full"></div>
      
      <div v-if="playError" class="flex flex-col items-center justify-center p-6 text-center max-w-lg text-white/90">
        <div class="text-amber-400 text-3xl mb-3">⚠️</div>
        <div class="font-medium text-base mb-2">播放失败</div>
        <p class="text-xs text-zinc-400 mb-4 leading-relaxed">{{ errorMessage }}</p>
        <t-link theme="primary" :href="videoUrl" target="_blank" download class="!text-white flex items-center gap-1 bg-[var(--color-primary)] px-4 py-1.5 rounded">
          <download-icon class="mr-1" /> 下载此文件
        </t-link>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

:deep(.t-dialog) {
  max-width: 95vw;
}

:deep(.t-dialog__header) {
  cursor: move;
  user-select: none;
}

:deep(.t-dialog__body) {
  padding: 0;
  background-color: #000;
}

.art-loading-indicator {
  padding: 6px 14px;
  background: rgba(0, 0, 0, 0.65);
  color: #fff;
  border-radius: 4px;
  font-size: 13px;
}
</style>
