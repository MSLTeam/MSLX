<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin } from 'tdesign-vue-next';
import { ImageIcon, RefreshIcon, UploadIcon } from 'tdesign-icons-vue-next';
import { downloadFileStream, finishUpload, initUpload, saveUploadedFile, uploadChunk } from '@/api/files';

const route = useRoute();
const instanceId = parseInt(route.params.serverId as string);

// --- 状态管理 ---
const loading = ref(false);
const errorMsg = ref('');
const currentIconUrl = ref<string | null>(null);

// --- 裁剪器相关状态 ---
const fileInput = ref<HTMLInputElement | null>(null);
const showCropDialog = ref(false);
const localImageSrc = ref('');
const sourceImageRef = ref<HTMLImageElement | null>(null);

const cropState = ref({
  x: 0,
  y: 0,
  size: 100, // 正方形尺寸
});
const cropperConfig = ref({
  imgWidth: 0,
  imgHeight: 0,
  scale: 1, // 显示比例与真实图片的比例
});

// 拖拽与缩放临时状态
let dragMode: 'move' | 'resize' | null = null;
let startX = 0;
let startY = 0;
let initialCropX = 0;
let initialCropY = 0;
let initialCropSize = 0;

// --- 获取当前图标 ---
const fetchCurrentIcon = async () => {
  loading.value = true;
  errorMsg.value = '';

  try {
    const blobData = await downloadFileStream(instanceId, 'server-icon.png');
    const blob = (blobData as any).data || blobData;

    if (blob instanceof Blob) {
      if (blob.size === 0 || blob.type.includes('json')) {
        // 404
        currentIconUrl.value = null;
      } else {
        if (currentIconUrl.value) URL.revokeObjectURL(currentIconUrl.value);
        currentIconUrl.value = URL.createObjectURL(blob);
      }
    } else {
      currentIconUrl.value = null;
    }
  } catch {
    currentIconUrl.value = null;
  } finally {
    loading.value = false;
  }
};

// --- 选择文件 ---
const triggerSelectFile = () => {
  if (fileInput.value) {
    fileInput.value.value = '';
    fileInput.value.click();
  }
};

const onFileSelect = (e: Event) => {
  const target = e.target as HTMLInputElement;
  const file = target.files?.[0];
  if (!file) return;

  if (!file.type.startsWith('image/')) {
    MessagePlugin.warning('请选择图片文件');
    return;
  }

  const reader = new FileReader();
  reader.onload = (event) => {
    localImageSrc.value = event.target?.result as string;
    showCropDialog.value = true;
  };
  reader.readAsDataURL(file);
};

// --- 裁剪器逻辑 ---
const initCropBox = () => {
  if (!sourceImageRef.value) return;
  const img = sourceImageRef.value;

  cropperConfig.value.imgWidth = img.clientWidth;
  cropperConfig.value.imgHeight = img.clientHeight;
  cropperConfig.value.scale = img.naturalWidth / img.clientWidth;

  const minSide = Math.min(img.clientWidth, img.clientHeight);
  const size = Math.floor(minSide * 1);
  cropState.value = {
    size,
    x: (img.clientWidth - size) / 2,
    y: (img.clientHeight - size) / 2,
  };
};

const startDrag = (e: MouseEvent, mode: 'move' | 'resize') => {
  e.preventDefault();
  dragMode = mode;
  startX = e.clientX;
  startY = e.clientY;
  initialCropX = cropState.value.x;
  initialCropY = cropState.value.y;
  initialCropSize = cropState.value.size;

  window.addEventListener('mousemove', handleDrag);
  window.addEventListener('mouseup', stopDrag);
};

const handleDrag = (e: MouseEvent) => {
  if (!dragMode) return;

  const dx = e.clientX - startX;
  const dy = e.clientY - startY;

  if (dragMode === 'move') {
    const newX = initialCropX + dx;
    const newY = initialCropY + dy;

    // 边界检测
    const maxX = cropperConfig.value.imgWidth - cropState.value.size;
    const maxY = cropperConfig.value.imgHeight - cropState.value.size;

    cropState.value.x = Math.max(0, Math.min(newX, maxX));
    cropState.value.y = Math.max(0, Math.min(newY, maxY));
  } else if (dragMode === 'resize') {
    // 缩放时保持正方形
    const dSize = Math.max(dx, dy);
    let newSize = initialCropSize + dSize;

    // 限制最小尺寸
    if (newSize < 20) newSize = 20;

    // 限制最大尺寸
    const maxAllowableSize = Math.min(
      cropperConfig.value.imgWidth - cropState.value.x,
      cropperConfig.value.imgHeight - cropState.value.y,
    );

    cropState.value.size = Math.min(newSize, maxAllowableSize);
  }
};

const stopDrag = () => {
  dragMode = null;
  window.removeEventListener('mousemove', handleDrag);
  window.removeEventListener('mouseup', stopDrag);
};

// --- 生成图片并上传 ---
const confirmCropAndUpload = async () => {
  if (!sourceImageRef.value) return;

  loading.value = true;

  try {
    const canvas = document.createElement('canvas');
    canvas.width = 64;
    canvas.height = 64;
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('无法初始化 Canvas');

    const img = sourceImageRef.value;
    const scale = cropperConfig.value.scale;

    // 计算真实图片上的裁剪坐标和尺寸
    const sourceX = cropState.value.x * scale;
    const sourceY = cropState.value.y * scale;
    const sourceSize = cropState.value.size * scale;

    ctx.drawImage(img, sourceX, sourceY, sourceSize, sourceSize, 0, 0, 64, 64);

    // 导出为 Blob
    const blob = await new Promise<Blob>((resolve, reject) => {
      canvas.toBlob((b) => {
        if (b) resolve(b);
        else reject(new Error('生成图片失败'));
      }, 'image/png');
    });

    // 开始上传流程
    const initRes = await initUpload();
    const uploadId = (initRes as any).uploadId || (initRes as any).data?.uploadId;
    if (!uploadId) throw new Error('初始化上传失败：未获取到 uploadId');

    await uploadChunk(uploadId, 0, blob);
    await finishUpload(uploadId, 1);

    // 指定保存到实例根目录的 server-icon.png
    await saveUploadedFile(instanceId, uploadId, 'server-icon.png', '');

    MessagePlugin.success('服务器图标已成功更新！');
    showCropDialog.value = false;

    // 重新拉取以预览新图标
    await fetchCurrentIcon();
  } catch (e: any) {
    MessagePlugin.error(e.message || '上传失败，请重试');
  } finally {
    loading.value = false;
  }
};

// --- 生命周期 ---
onMounted(() => {
  fetchCurrentIcon();
});
</script>

<template>
  <div class="flex flex-col mx-auto w-full pb-6">

    <div v-if="errorMsg" class="mb-4">
      <t-alert theme="error" :message="errorMsg" closeable class="!rounded-xl shadow-sm border border-red-100 dark:border-red-900/50" @close="errorMsg = ''">
        <template #operation>
          <span class="cursor-pointer ml-2 font-bold text-red-600 dark:text-red-400 hover:opacity-80 transition-opacity" @click="fetchCurrentIcon">重试</span>
        </template>
      </t-alert>
    </div>

    <t-loading :loading="loading" show-overlay>

      <div class="flex items-center gap-2 mt-5 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60">
        <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
        <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">外观设置</h2>
      </div>

      <div class="flex flex-col md:flex-row md:items-center justify-between py-4 gap-4">

        <div class="flex-1 md:pr-8">
          <div class="text-sm font-bold text-[var(--td-text-color-primary)]">服务器图标</div>
          <div class="text-xs text-[var(--td-text-color-secondary)] mt-1.5 leading-relaxed">
            上传自定义的 JPG / PNG 图片替换现有的 server-icon.png。<br>
            系统将提供可视化裁剪工具，并自动帮您转换为标准的 64x64 服务器图标文件。
          </div>
        </div>

        <div class="flex items-center gap-4 shrink-0 w-full md:w-auto mt-2 md:mt-0">

          <input ref="fileInput" type="file" accept="image/png, image/jpeg" class="hidden" @change="onFileSelect" />

          <div class="w-[72px] h-[72px] shrink-0 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl flex justify-center items-center bg-zinc-50 dark:bg-zinc-900/50 overflow-hidden shadow-inner">
            <template v-if="currentIconUrl">
              <img :src="currentIconUrl" alt="Server Icon" class="w-16 h-16 rounded shadow-sm [image-rendering:pixelated]" />
            </template>
            <template v-else>
              <div class="flex flex-col items-center text-[var(--td-text-color-secondary)] gap-1 opacity-80">
                <image-icon size="20px" />
                <span class="text-[10px] font-medium tracking-widest">暂无</span>
              </div>
            </template>
          </div>

          <div class="flex flex-col gap-2 flex-1 md:flex-none md:w-[140px]">
            <t-button theme="primary" block class="!rounded-lg shadow-sm !m-0" @click="triggerSelectFile">
              <template #icon><upload-icon /></template> 选择新图标
            </t-button>
            <t-button variant="outline" block class="!rounded-lg !bg-zinc-50 dark:!bg-zinc-800/50 !border-zinc-200 dark:!border-zinc-700 hover:!bg-zinc-100 dark:hover:!bg-zinc-800 !text-zinc-700 dark:!text-zinc-300 transition-colors !m-0" @click="fetchCurrentIcon">
              <template #icon><refresh-icon /></template> 刷新图标
            </t-button>
          </div>

        </div>
      </div>

    </t-loading>

    <t-dialog
      v-model:visible="showCropDialog"
      header="裁剪服务器图标 (64x64)"
      width="600px"
      :close-on-overlay-click="false"
      attach="body"
      @confirm="confirmCropAndUpload"
    >
      <div v-loading="loading" class="flex flex-col items-center p-5 md:p-6 bg-zinc-50/50 dark:bg-zinc-950/20">

        <p class="text-xs text-[var(--td-text-color-secondary)] mb-5 text-center bg-[var(--td-bg-color-container)]/80 px-4 py-2.5 rounded-lg border border-zinc-200/60 dark:border-zinc-700/60 shadow-sm backdrop-blur-md">
          请拖动和缩放亮色方框，选择需要截取的区域。生成后将自动转为 <b class="text-zinc-700 dark:text-zinc-300">64x64</b> 的标准尺寸。
        </p>

        <div v-if="localImageSrc" class="relative max-w-full max-h-[400px] select-none cropper-bg-pattern rounded-lg overflow-hidden border border-zinc-200/80 dark:border-zinc-700/80 shadow-inner">

          <img ref="sourceImageRef" :src="localImageSrc" class="block max-w-full max-h-[400px]" draggable="false" @load="initCropBox" />

          <div class="absolute inset-0 bg-black/60 pointer-events-none"></div>

          <div
            class="absolute cursor-move overflow-hidden shadow-[0_0_0_1px_rgba(0,0,0,0.5)] ring-1 ring-white/50"
            :style="{
              left: cropState.x + 'px',
              top: cropState.y + 'px',
              width: cropState.size + 'px',
              height: cropState.size + 'px',
            }"
            @mousedown="(e) => startDrag(e, 'move')"
          >
            <img
              :src="localImageSrc"
              class="absolute top-0 left-0 max-w-none pointer-events-none"
              draggable="false"
              :style="{
                width: cropperConfig.imgWidth + 'px',
                height: cropperConfig.imgHeight + 'px',
                transform: `translate(${-cropState.x}px, ${-cropState.y}px)`,
              }"
            />

            <div class="absolute inset-0 border border-dashed border-white/80 pointer-events-none"></div>

            <div
              class="absolute right-0 bottom-0 w-3 h-3 bg-[var(--color-primary)] border-2 border-white cursor-nwse-resize z-10 before:absolute before:-inset-2.5"
              @mousedown.stop="(e) => startDrag(e, 'resize')"
            ></div>
          </div>

        </div>
      </div>
    </t-dialog>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* === 裁剪器专用的透明棋盘 === */
.cropper-bg-pattern {
  background-image: repeating-conic-gradient(#e4e4e7 0% 25%, transparent 0% 50%);
  background-size: 20px 20px;
  background-color: #f4f4f5;
}

:global(html[theme-mode='dark']) .cropper-bg-pattern,
:global(html.dark) .cropper-bg-pattern {
  background-image: repeating-conic-gradient(#3f3f46 0% 25%, transparent 0% 50%);
  background-color: #27272a;
}
</style>
