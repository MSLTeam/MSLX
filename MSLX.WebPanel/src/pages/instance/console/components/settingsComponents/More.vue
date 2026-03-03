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
  <div class="settings-container">

    <div v-if="errorMsg" class="error-alert-bar">
      <t-alert theme="error" :message="errorMsg" closeable @close="errorMsg = ''">
        <template #operation>
          <span style="cursor: pointer; margin-left: 8px" @click="fetchCurrentIcon">重试</span>
        </template>
      </t-alert>
    </div>

    <t-loading :loading="loading" show-overlay>

      <div class="setting-group-title">外观设置</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">服务器图标</div>
          <div class="desc">
            上传自定义的 JPG / PNG 图片替换现有的 server-icon.png。<br>
            MSLX将提供可视化裁剪工具，并自动帮您转换为标准的 64x64 服务器图标文件。
          </div>
        </div>

        <div class="setting-control icon-control-layout">
          <input
            ref="fileInput"
            type="file"
            accept="image/png, image/jpeg"
            style="display: none"
            @change="onFileSelect"
          />

          <div class="icon-preview-box">
            <template v-if="currentIconUrl">
              <img :src="currentIconUrl" alt="Server Icon" class="server-icon-img" />
            </template>
            <template v-else>
              <div class="empty-icon">
                <image-icon size="24px" />
                <span>暂无</span>
              </div>
            </template>
          </div>

          <div class="icon-actions">
            <t-button theme="primary" block @click="triggerSelectFile">
              <template #icon><upload-icon /></template>
              选择新图标
            </t-button>
            <t-button style="margin-left: 0;" variant="outline" block @click="fetchCurrentIcon">
              <template #icon><refresh-icon /></template>
              刷新图标
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
      @confirm="confirmCropAndUpload"
    >
      <div v-loading="loading" class="crop-dialog-content">
        <p class="crop-tip">请拖动和缩放亮色方框，选择需要截取的区域。生成后将自动转为 64x64 的标准尺寸。</p>

        <div v-if="localImageSrc" class="cropper-container">
          <img ref="sourceImageRef" :src="localImageSrc" class="cropper-bg" draggable="false" @load="initCropBox" />

          <div class="cropper-overlay"></div>

          <div
            class="crop-selection"
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
              class="cropper-inner-img"
              draggable="false"
              :style="{
                width: cropperConfig.imgWidth + 'px',
                height: cropperConfig.imgHeight + 'px',
                transform: `translate(${-cropState.x}px, ${-cropState.y}px)`,
              }"
            />

            <div class="crop-borders"></div>
            <div class="resize-handle" @mousedown.stop="(e) => startDrag(e, 'resize')"></div>
          </div>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
/* 全局容器结构完全复刻设置页 */
.settings-container {
  margin: 0 auto;
  padding-bottom: 24px;
}

.error-alert-bar {
  margin-top: 16px;
  margin-bottom: 16px;
}

/* 统一的分组标题样式 */
.setting-group-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  margin-top: 32px;
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);
  display: flex;
  align-items: center;

  &::before {
    content: '';
    display: inline-block;
    width: 4px;
    height: 16px;
    background-color: var(--td-brand-color);
    margin-right: 8px;
    border-radius: 2px;
  }

  /* 第一个标题取消顶部边距，视觉更好 */
  &:first-of-type {
    margin-top: 24px;
  }
}

/* 统一的基础列表项样式 */
.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 32px 16px 0;
  flex-wrap: wrap; /* 允许在手机端换行 */

  .setting-info {
    flex: 1;
    padding-right: 32px;
    min-width: 200px; /* 防止过窄 */

    .title {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 500;
      line-height: 22px;
    }

    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
      line-height: 20px;
    }
  }

  .setting-control {
    width: 340px; /* 强制统一控制区宽度 */
    flex-shrink: 0;
  }
}

/* 图标特供的控制区内部排版 */
.icon-control-layout {
  display: flex;
  align-items: center;
  gap: 16px;

  /* 左侧：图标预览框 */
  .icon-preview-box {
    width: 72px;
    height: 72px;
    flex-shrink: 0;
    border: 1px dashed var(--td-component-border);
    border-radius: var(--td-radius-default);
    display: flex;
    justify-content: center;
    align-items: center;
    background-color: var(--td-bg-color-secondarycontainer);
    overflow: hidden;

    .server-icon-img {
      width: 64px;
      height: 64px;
      image-rendering: pixelated; /* 保持原汁原味的像素清晰度 */
    }

    .empty-icon {
      display: flex;
      flex-direction: column;
      align-items: center;
      color: var(--td-text-color-placeholder);
      font-size: 12px;
      gap: 4px;
    }
  }

  /* 右侧：按钮上下排列 */
  .icon-actions {
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
}

/* --- 弹窗内的独立裁剪器样式 --- */
.crop-dialog-content {
  display: flex;
  flex-direction: column;
  align-items: center;

  .crop-tip {
    font-size: 13px;
    color: var(--td-text-color-secondary);
    margin-bottom: 16px;
    text-align: center;
  }
}

.cropper-container {
  position: relative;
  max-width: 100%;
  max-height: 400px;
  background-image: repeating-conic-gradient(#f0f0f0 0% 25%, transparent 0% 50%);
  background-size: 20px 20px;
  background-color: #fff;
  user-select: none;

  .cropper-bg {
    display: block;
    max-width: 100%;
    max-height: 400px;
  }

  .cropper-overlay {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: rgba(0, 0, 0, 0.5);
    pointer-events: none;
  }

  .crop-selection {
    position: absolute;
    cursor: move;
    overflow: hidden;

    .cropper-inner-img {
      position: absolute;
      top: 0;
      left: 0;
      max-width: none;
      pointer-events: none;
    }

    .crop-borders {
      position: absolute;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      border: 1px dashed #fff;
      box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.3);
      pointer-events: none;
    }

    .resize-handle {
      position: absolute;
      right: 0;
      bottom: 0;
      width: 12px;
      height: 12px;
      background-color: var(--td-brand-color);
      border: 2px solid #fff;
      cursor: nwse-resize;
      z-index: 10;

      &::after {
        content: '';
        position: absolute;
        top: -10px;
        left: -10px;
        right: -10px;
        bottom: -10px;
      }
    }
  }
}

/* 响应式：折叠排版 */
@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;

    .setting-info {
      padding-right: 0;
      margin-bottom: 12px;
    }

    .setting-control {
      width: 100%; /* 移动端占满宽度 */
    }
  }
}
</style>
