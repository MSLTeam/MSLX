<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue';
import {
  AppIcon,
  CodeIcon,
  DeleteIcon,
  DownloadIcon,
  EarthIcon,
  EditIcon,
  FileIcon,
  FileImageIcon,
  ImageIcon,
  FilePasteIcon,
  FileZipIcon,
  FolderIcon,
  LockOnIcon,
  MoreIcon,
  PlayCircleFilledIcon,
  SettingIcon,
  VideoIcon,
} from 'tdesign-icons-vue-next';
import type { FilesListModel } from '@/api/model/files';
import { getFileThumbnailUrl, getVideoStreamUrl } from '@/api/files';

const props = defineProps<{
  fileList: FilesListModel[];
  selectedRowKeys: string[];
  instanceId: number;
  currentPath: string;
  isMobile?: boolean;
  hasPermissionSupport?: boolean;
  loading?: boolean;
}>();

const emit = defineEmits<{
  'update:selectedRowKeys': [keys: string[]];
  'row-click': [row: FilesListModel];
  'open-editor': [fileName: string];
  'open-preview': [fileName: string];
  'open-video-preview': [fileName: string];
  download: [row: FilesListModel];
  rename: [row: FilesListModel];
  delete: [row: FilesListModel];
  compress: [];
  decompress: [row: FilesListModel];
  permission: [row: FilesListModel];
}>();

// 视口内卡片懒加载状态追踪
const visibleItems = ref<Record<string, boolean>>({});
// 图片加载失败追踪
const imageLoadErrors = ref<Record<string, boolean>>({});

// 视频缩略图缓存与状态追踪
const videoThumbnails = ref<Record<string, string>>({});
const videoStatuses = ref<Record<string, 'pending' | 'loading' | 'done' | 'error'>>({});

// 视频抽帧任务并发控制队列 (最大并发数 2)
const MAX_VIDEO_CONCURRENCY = 2;
let activeVideoWorkers = 0;
const videoQueue: Array<() => Promise<void>> = [];

const processVideoQueue = () => {
  if (activeVideoWorkers >= MAX_VIDEO_CONCURRENCY || videoQueue.length === 0) return;
  const nextTask = videoQueue.shift();
  if (nextTask) {
    activeVideoWorkers++;
    nextTask().finally(() => {
      activeVideoWorkers--;
      processVideoQueue();
    });
  }
};

const enqueueVideoThumbnail = (fileName: string, fullPath: string) => {
  if (videoThumbnails.value[fullPath] || videoStatuses.value[fullPath]) return;

  videoStatuses.value[fullPath] = 'pending';

  const task = () =>
    new Promise<void>((resolve) => {
      videoStatuses.value[fullPath] = 'loading';

      const video = document.createElement('video');
      video.crossOrigin = 'anonymous';
      video.preload = 'metadata';
      video.muted = true;
      (video as any).playsInline = true;

      const videoUrl = getVideoStreamUrl(props.instanceId, fullPath);
      let isResolved = false;

      const cleanup = () => {
        if (!isResolved) {
          isResolved = true;
          video.removeAttribute('src');
          video.load();
          resolve();
        }
      };

      // 6秒超时容灾（解码失败/太慢了）
      const timeoutId = setTimeout(() => {
        videoStatuses.value[fullPath] = 'error';
        cleanup();
      }, 6000);

      video.addEventListener('loadedmetadata', () => {
        // 定位到第 1 秒，若视频很短则定位到 0.1 秒
        video.currentTime = video.duration > 1.0 ? 1.0 : 0.1;
      });

      video.addEventListener('seeked', () => {
        clearTimeout(timeoutId);
        try {
          const canvas = document.createElement('canvas');
          const maxDim = 320;
          let w = video.videoWidth || 320;
          let h = video.videoHeight || 180;
          if (w > maxDim || h > maxDim) {
            const scale = Math.min(maxDim / w, maxDim / h);
            w = Math.round(w * scale);
            h = Math.round(h * scale);
          }
          canvas.width = w;
          canvas.height = h;

          const ctx = canvas.getContext('2d');
          if (ctx) {
            ctx.drawImage(video, 0, 0, w, h);
            const dataUrl = canvas.toDataURL('image/jpeg', 0.75);
            videoThumbnails.value[fullPath] = dataUrl;
            videoStatuses.value[fullPath] = 'done';
          } else {
            videoStatuses.value[fullPath] = 'error';
          }
        } catch (e) {
          console.warn('提取视频首帧失败:', fileName, e);
          videoStatuses.value[fullPath] = 'error';
        }
        cleanup();
      });

      video.addEventListener('error', () => {
        clearTimeout(timeoutId);
        videoStatuses.value[fullPath] = 'error';
        cleanup();
      });

      video.src = videoUrl;
    });

  videoQueue.push(task);
  processVideoQueue();
};

// 格式与类型判断
const isImage = (name: string) => {
  const ext = name.split('.').pop()?.toLowerCase();
  return ['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp', 'bmp', 'svg'].includes(ext || '');
};

const isVideo = (name: string) => {
  const ext = name.split('.').pop()?.toLowerCase();
  return ['mp4', 'webm', 'ogg', 'mov', 'mkv', 'flv', 'avi', 'm4v', 'ts', '3gp'].includes(ext || '');
};

const isArchive = (name: string) => {
  const lower = name.toLowerCase();
  return /\.(zip|jar|rar|7z|tar|tar\.gz|tgz|tar\.xz|txz|tar\.bz2|tbz2|tar\.zst|gz|xz|bz2)$/i.test(lower);
};

const getFileIcon = (row: FilesListModel) => {
  if (row.type === 'folder') {
    const name = row.name.toLowerCase();
    if (name === 'config' || name === 'settings') return { icon: SettingIcon, color: 'var(--td-warning-color)' };
    if (name.startsWith('world') || name === 'level') return { icon: EarthIcon, color: 'var(--td-success-color)' };
    if (['plugins', 'mods', 'libraries'].includes(name)) return { icon: AppIcon, color: 'var(--td-brand-color)' };
    if (['logs', 'crash-reports', 'cache', 'temp'].includes(name))
      return { icon: FolderIcon, color: 'var(--td-gray-color-6)' };
    return { icon: FolderIcon, color: 'var(--td-brand-color)' };
  }
  const ext = row.name.split('.').pop()?.toLowerCase();
  if (['png', 'jpg', 'jpeg', 'gif', 'ico', 'webp'].includes(ext || ''))
    return { icon: FileImageIcon, color: 'var(--td-success-color)' };
  if (isVideo(row.name)) return { icon: VideoIcon, color: '#0052d9' };
  if (isArchive(row.name)) return { icon: FileZipIcon, color: '#722ed1' };
  if (['yml', 'yaml', 'json', 'properties', 'toml', 'xml', 'conf', 'sh', 'bat', 'cmd'].includes(ext || ''))
    return { icon: CodeIcon, color: 'var(--td-warning-color)' };
  if (['log', 'txt', 'md', 'lock'].includes(ext || '')) return { icon: FilePasteIcon, color: 'var(--td-gray-color-6)' };
  return { icon: FileIcon, color: 'var(--td-text-color-secondary)' };
};

const formatSize = (size: number) => {
  if (size === 0) return '-';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  let s = size;
  while (s >= 1024 && i < units.length - 1) {
    s /= 1024;
    i++;
  }
  return `${s.toFixed(1)} ${units[i]}`;
};

const formatTime = (timeStr: string) => {
  if (!timeStr) return '-';
  const date = new Date(timeStr);
  return `${date.getMonth() + 1}/${date.getDate()} ${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}`;
};

// 勾选操作逻辑
const isSelected = (name: string) => props.selectedRowKeys.includes(name);

const toggleSelect = (name: string, e?: Event) => {
  e?.stopPropagation();
  const index = props.selectedRowKeys.indexOf(name);
  const next = [...props.selectedRowKeys];
  if (index > -1) {
    next.splice(index, 1);
  } else {
    next.push(name);
  }
  emit('update:selectedRowKeys', next);
};

const handleCardClick = (item: FilesListModel, e: MouseEvent) => {
  if (e.ctrlKey || e.metaKey || e.shiftKey) {
    toggleSelect(item.name, e);
    return;
  }
  emit('row-click', item);
};

// 视口监听
let observer: IntersectionObserver | null = null;
const cardRefs = new Map<string, HTMLElement>();

const setCardRef = (name: string, el: any) => {
  if (el) {
    cardRefs.set(name, el as HTMLElement);
    if (observer) observer.observe(el);
  } else {
    const existing = cardRefs.get(name);
    if (existing && observer) observer.unobserve(existing);
    cardRefs.delete(name);
  }
};

const initObserver = () => {
  if (typeof IntersectionObserver === 'undefined') {
    // 兜底环境：直接全量可见
    props.fileList.forEach((f) => {
      visibleItems.value[f.name] = true;
    });
    return;
  }

  observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          const name = (entry.target as HTMLElement).dataset.fileName;
          if (name) {
            visibleItems.value[name] = true;
            const fullPath = props.currentPath ? `${props.currentPath}/${name}` : name;
            if (isVideo(name)) {
              enqueueVideoThumbnail(name, fullPath);
            }
            // 进入视口触发后即可解绑该元素，降低 observer 负担
            observer?.unobserve(entry.target);
          }
        }
      });
    },
    {
      rootMargin: '200px 0px', // 提前 200px 预加载，保证滚动流畅
      threshold: 0.01,
    },
  );

  cardRefs.forEach((el) => observer?.observe(el));
};

watch(
  () => props.fileList,
  () => {
    // 列表变更时观察新元素
    visibleItems.value = {};
    if (observer) {
      observer.disconnect();
      setTimeout(() => {
        cardRefs.forEach((el) => observer?.observe(el));
      }, 50);
    }
  },
  { deep: false },
);

onMounted(() => {
  initObserver();
});

onUnmounted(() => {
  if (observer) {
    observer.disconnect();
    observer = null;
  }
  cardRefs.clear();
  videoQueue.length = 0;
});
</script>

<template>
  <div class="p-3 md:p-5 w-full">
    <!-- 空状态 -->
    <div
      v-if="!fileList || fileList.length === 0"
      class="py-24 flex flex-col items-center justify-center text-[var(--td-text-color-secondary)]"
    >
      <file-icon size="48px" class="opacity-40 mb-3" />
      <span class="text-sm font-medium">当前目录下暂无文件</span>
    </div>

    <!-- 大图标网格列表 -->
    <div
      v-else
      class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 2xl:grid-cols-8 gap-3 md:gap-4"
    >
      <div
        v-for="item in fileList"
        :key="item.name"
        :ref="(el) => setCardRef(item.name, el)"
        :data-file-name="item.name"
        class="file-grid-card design-card group relative flex flex-col rounded-xl border transition-all duration-200 cursor-pointer overflow-hidden select-none hover:shadow-md backdrop-blur-sm"
        :class="[
          isSelected(item.name)
            ? '!border-[var(--color-primary)] ring-2 ring-[var(--color-primary)]/30 !bg-[var(--color-primary)]/10'
            : 'border-zinc-200/70 dark:border-zinc-800 hover:border-[var(--color-primary)]/50',
        ]"
        @click="handleCardClick(item, $event)"
      >
        <!-- 左上角勾选框 (悬浮/选中时显示) -->
        <div
          class="absolute top-2.5 left-2.5 z-20 transition-all duration-150 cursor-pointer flex items-center justify-center"
          :class="[isSelected(item.name) ? 'opacity-100 scale-100' : 'opacity-0 group-hover:opacity-100 scale-90 group-hover:scale-100']"
          @click.stop="toggleSelect(item.name, $event)"
        >
          <t-checkbox
            :checked="isSelected(item.name)"
            class="custom-card-checkbox pointer-events-none"
          />
        </div>

        <!-- 右上角更多操作下拉按钮 (悬浮时显示) -->
        <div
          class="absolute top-2.5 right-2.5 z-20 opacity-0 group-hover:opacity-100 transition-opacity duration-150"
          @click.stop
        >
          <t-dropdown :placement="isMobile ? 'bottom-right' : 'bottom'">
            <button
              class="w-6 h-6 rounded-md flex items-center justify-center bg-white/90 dark:bg-zinc-900/90 shadow-sm border border-zinc-200 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:text-[var(--color-primary)] hover:border-[var(--color-primary)] transition-colors"
            >
              <more-icon size="14px" />
            </button>
            <template #dropdown>
              <t-dropdown-menu>
                <t-dropdown-item
                  v-if="isArchive(item.name) && item.type !== 'folder'"
                  value="decompress"
                  @click="emit('decompress', item)"
                >
                  <file-zip-icon class="mr-2" /> <span>解压</span>
                </t-dropdown-item>

                <t-dropdown-item
                  v-if="!(item.type === 'folder' || isArchive(item.name))"
                  value="edit"
                  @click="
                    isVideo(item.name)
                      ? emit('open-video-preview', item.name)
                      : isImage(item.name)
                      ? emit('open-preview', item.name)
                      : emit('open-editor', item.name)
                  "
                >
                  <video-icon v-if="isVideo(item.name)" class="mr-2" />
                  <image-icon v-else-if="isImage(item.name)" class="mr-2" />
                  <edit-icon v-else class="mr-2" />
                  <span>{{ isVideo(item.name) || isImage(item.name) ? '预览' : '编辑' }}</span>
                </t-dropdown-item>

                <t-dropdown-item
                  v-if="hasPermissionSupport"
                  value="permission"
                  @click="emit('permission', item)"
                >
                  <lock-on-icon class="mr-2" /> <span>权限</span>
                </t-dropdown-item>

                <t-dropdown-item value="download" @click="emit('download', item)">
                  <download-icon class="mr-2" /> <span>下载</span>
                </t-dropdown-item>

                <t-dropdown-item value="rename" @click="emit('rename', item)">
                  <edit-icon class="mr-2" /> <span>重命名</span>
                </t-dropdown-item>

                <t-dropdown-item
                  value="delete"
                  class="danger-item !text-red-500 hover:!bg-red-50 dark:hover:!bg-red-500/10 transition-colors"
                  @click="emit('delete', item)"
                >
                  <delete-icon class="mr-2" /> <span>删除</span>
                </t-dropdown-item>
              </t-dropdown-menu>
            </template>
          </t-dropdown>
        </div>

        <!-- 媒体预览 / 图标展示区域 (宽高比 1:1) -->
        <div
          class="relative w-full aspect-square bg-black/[0.02] dark:bg-white/[0.03] flex items-center justify-center overflow-hidden border-b border-zinc-200/50 dark:border-zinc-700/40"
        >
          <!-- 图片类型 -->
          <template v-if="isImage(item.name) && item.type !== 'folder'">
            <img
              v-if="visibleItems[item.name] && !imageLoadErrors[item.name]"
              :src="getFileThumbnailUrl(instanceId, currentPath ? `${currentPath}/${item.name}` : item.name, 256)"
              :alt="item.name"
              loading="lazy"
              class="w-full h-full object-cover transition-transform duration-300 group-hover:scale-105"
              @error="imageLoadErrors[item.name] = true"
            />
            <div v-else class="flex flex-col items-center justify-center p-3">
              <file-image-icon class="text-4xl" style="color: var(--td-success-color)" />
            </div>
            <!-- 图片角标 -->
            <div
              class="absolute bottom-1.5 right-1.5 px-1.5 py-0.5 rounded text-[10px] font-medium tracking-wider bg-black/50 text-white backdrop-blur-sm pointer-events-none uppercase"
            >
              {{ item.name.split('.').pop() }}
            </div>
          </template>

          <!-- 视频类型 -->
          <template v-else-if="isVideo(item.name) && item.type !== 'folder'">
            <div class="relative w-full h-full flex items-center justify-center overflow-hidden">
              <img
                v-if="videoThumbnails[currentPath ? `${currentPath}/${item.name}` : item.name]"
                :src="videoThumbnails[currentPath ? `${currentPath}/${item.name}` : item.name]"
                :alt="item.name"
                class="w-full h-full object-cover transition-transform duration-300 group-hover:scale-105"
              />
              <div v-else class="flex flex-col items-center justify-center p-3">
                <video-icon class="text-4xl text-[#0052d9] transition-transform group-hover:scale-110" />
              </div>

              <!-- 视频播放标记蒙层/角标 -->
              <div
                class="absolute inset-0 flex items-center justify-center bg-black/10 group-hover:bg-black/25 transition-colors pointer-events-none"
              >
                <play-circle-filled-icon
                  class="text-white/85 group-hover:text-white drop-shadow-md text-3xl transition-transform group-hover:scale-110"
                />
              </div>

              <div
                class="absolute bottom-1.5 right-1.5 px-1.5 py-0.5 rounded text-[10px] font-medium tracking-wider bg-black/60 text-white backdrop-blur-sm pointer-events-none uppercase"
              >
                {{ item.name.split('.').pop() }}
              </div>
            </div>
          </template>

          <!-- 文件夹 / 其它常规文件类型 -->
          <template v-else>
            <div class="flex flex-col items-center justify-center p-4">
              <component
                :is="getFileIcon(item).icon"
                class="text-5xl transition-transform duration-300 group-hover:scale-110"
                :style="{ color: getFileIcon(item).color }"
              />
            </div>
          </template>
        </div>

        <!-- 底部文件名与信息区域 -->
        <div class="p-2.5 flex flex-col justify-between flex-1 bg-transparent">
          <t-tooltip :content="item.name" placement="top" :show-arrow="false">
            <span
              class="font-medium text-[13px] text-[var(--td-text-color-primary)] group-hover:text-[var(--color-primary)] transition-colors line-clamp-2 leading-snug break-all text-center"
            >
              {{ item.name }}
            </span>
          </t-tooltip>

          <div
            class="mt-1.5 flex items-center justify-between text-[11px] text-[var(--td-text-color-secondary)] font-mono px-0.5"
          >
            <span>{{ item.type === 'folder' ? '文件夹' : formatSize(item.size) }}</span>
            <span>{{ formatTime(item.lastModified) }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

.file-grid-card {
  will-change: transform, box-shadow;
}

:deep(.custom-card-checkbox.t-checkbox) {
  margin: 0 !important;
  padding: 0 !important;
  display: inline-flex !important;
  align-items: center !important;
  justify-content: center !important;
  line-height: 1 !important;
  vertical-align: middle;

  .t-checkbox__label {
    display: none !important;
  }

  .t-checkbox__input {
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.25);
    transform: scale(1.15);
    transform-origin: center;
    transition: transform 0.15s ease, background-color 0.2s cubic-bezier(0.82, 0, 1, 0.9);
  }

  &.t-is-checked .t-checkbox__input {
    background-color: var(--color-primary) !important;
    border-color: var(--color-primary) !important;
  }
}
</style>
