<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { useUserStore } from '@/store';
import { LocationIcon, HomeIcon } from 'tdesign-icons-vue-next';
import { getWorldSpawn } from '@/api/instance';

const props = defineProps<{
  visible: boolean;
  serverId: number;
}>();

const emits = defineEmits<{
  'update:visible': [value: boolean];
}>();

const userStore = useUserStore();
const { baseUrl, token } = userStore;

const scale = ref(1);
const translateX = ref(0);
const translateY = ref(0);
let isDragging = false;
let startX = 0;
let startY = 0;

const inputX = ref<number | null>(0);
const inputZ = ref<number | null>(0);

interface RegionTile {
  x: number;
  z: number;
  key: string;
  src: string;
  loaded: boolean;
}
const loadedRegions = ref<RegionTile[]>([]);

const centerCoordinates = computed(() => {
  const worldX = -translateX.value / scale.value;
  const worldZ = -translateY.value / scale.value;

  return {
    blockX: Math.round(worldX),
    blockZ: Math.round(worldZ),
    regionX: Math.round(worldX / 512),
    regionZ: Math.round(worldZ / 512),
  };
});

const jumpToCoordinates = (targetX: number, targetZ: number) => {
  translateX.value = -targetX * scale.value;
  translateY.value = -targetZ * scale.value;
  updateVisibleRegions();
};

const handleSearchJump = () => {
  const x = Number(inputX.value) || 0;
  const z = Number(inputZ.value) || 0;
  jumpToCoordinates(x, z);
};

const jumpToSpawn = async () => {
  try {
    const spawn = await getWorldSpawn(props.serverId);
    inputX.value = spawn.x;
    inputZ.value = spawn.z;
    jumpToCoordinates(spawn.x, spawn.z);
  } catch {
    inputX.value = 0;
    inputZ.value = 0;
    jumpToCoordinates(0, 0);
  }
};

const updateVisibleRegions = () => {
  const cx = centerCoordinates.value.regionX;
  const cz = centerCoordinates.value.regionZ;
  const missingTiles = [];

  for (let dx = -1; dx <= 1; dx++) {
    for (let dz = -1; dz <= 1; dz++) {
      const rx = cx + dx;
      const rz = cz + dz;
      const key = `${rx}_${rz}`;

      if (!loadedRegions.value.find((r) => r.key === key)) {
        const distance = dx * dx + dz * dz;
        missingTiles.push({ x: rx, z: rz, key, distance });
      }
    }
  }

  missingTiles.sort((a, b) => a.distance - b.distance);

  missingTiles.forEach((item) => {
    const host = baseUrl || '';
    const src = `${host}/api/instance/map/${props.serverId}/${item.x}/${item.z}?x-user-token=${token}`;

    loadedRegions.value.push({
      x: item.x,
      z: item.z,
      key: item.key,
      src,
      loaded: false,
    });
  });
};

watch(
  () => props.visible,
  (val) => {
    if (val) {
      scale.value = 1;
      loadedRegions.value = [];
      jumpToSpawn();
    } else {
      loadedRegions.value = [];
    }
  },
);

const startDrag = (e: MouseEvent) => {
  isDragging = true;
  startX = e.clientX - translateX.value;
  startY = e.clientY - translateY.value;
};

const onDrag = (e: MouseEvent) => {
  if (!isDragging) return;
  translateX.value = e.clientX - startX;
  translateY.value = e.clientY - startY;
  updateVisibleRegions();
};

const stopDrag = () => {
  isDragging = false;
};

const handleScroll = (e: WheelEvent) => {
  e.preventDefault();
  const zoomDirection = e.deltaY > 0 ? -0.1 : 0.1;
  const newScale = Math.min(Math.max(0.2, scale.value + zoomDirection), 5);

  const scaleRatio = newScale / scale.value;
  translateX.value = translateX.value * scaleRatio;
  translateY.value = translateY.value * scaleRatio;
  scale.value = newScale;

  updateVisibleRegions();
};

const handleClose = () => emits('update:visible', false);
</script>

<template>
  <t-dialog
    attach="body"
    :visible="visible"
    header="世界地图查看器"
    width="min(1000px, 95vw)"
    placement="center"
    :footer="false"
    @close="handleClose"
  >
    <div
      class="w-full h-[70vh] min-h-[500px] relative overflow-hidden bg-zinc-50 dark:bg-zinc-950 rounded-b-xl select-none cursor-grab active:cursor-grabbing custom-grid-bg"
      @wheel="handleScroll"
      @mousedown="startDrag"
      @mousemove="onDrag"
      @mouseup="stopDrag"
      @mouseleave="stopDrag"
    >

      <div
        class="absolute top-1/2 left-1/2 origin-center transition-transform duration-[50ms] ease-linear"
        :style="{ transform: `translate(${translateX}px, ${translateY}px) scale(${scale})` }"
      >
        <div
          v-for="tile in loadedRegions"
          :key="tile.key"
          class="absolute w-[512px] h-[512px] -ml-[256px] -mt-[256px] flex items-center justify-center bg-zinc-100/50 dark:bg-zinc-900/50"
          :style="{ left: `${tile.x * 512}px`, top: `${tile.z * 512}px` }"
        >
          <img
            :src="tile.src"
            class="w-full h-full pointer-events-none opacity-0 transition-opacity duration-500 ease-out [image-rendering:pixelated]"
            :class="{ 'opacity-100': tile.loaded }"
            @load="tile.loaded = true"
            alt="tile"
            draggable="false"
          />
        </div>
      </div>

      <div class="absolute top-1/2 left-1/2 w-3.5 h-3.5 -mt-[7px] -ml-[7px] pointer-events-none z-10 before:absolute before:left-[6px] before:top-0 before:w-[2px] before:h-[14px] before:bg-red-500/80 before:shadow-[0_0_2px_rgba(0,0,0,0.5)] after:absolute after:left-0 after:top-[6px] after:w-[14px] after:h-[2px] after:bg-red-500/80 after:shadow-[0_0_2px_rgba(0,0,0,0.5)]"></div>

      <div
        class="absolute top-4 left-4 p-3 flex items-center gap-2 bg-[var(--td-bg-color-container)]/80 backdrop-blur-md rounded-xl border border-[var(--td-component-border)] shadow-md z-20 cursor-default"
        @mousedown.stop
        @wheel.stop
      >
        <t-input v-model="inputX" type="number" placeholder="X 坐标" class="!w-[90px]" @enter="handleSearchJump" />
        <t-input v-model="inputZ" type="number" placeholder="Z 坐标" class="!w-[90px]" @enter="handleSearchJump" />

        <t-button theme="primary" class="!rounded-lg shadow-sm" @click="handleSearchJump">
          <template #icon><location-icon /></template> 定位
        </t-button>

        <t-button variant="outline" class="!rounded-lg !bg-zinc-100 dark:!bg-zinc-800 !border-zinc-200 dark:!border-zinc-700 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 !text-zinc-600 dark:!text-zinc-300 transition-colors" @click="jumpToSpawn" title="回到世界出生点">
          <template #icon><home-icon /></template>
        </t-button>
      </div>

      <div class="absolute bottom-4 right-4 p-4 flex flex-col font-mono text-sm bg-[var(--td-bg-color-container)]/80 backdrop-blur-md rounded-xl border border-[var(--td-component-border)] shadow-md z-20 pointer-events-none">

        <div class="flex justify-between items-center gap-6 mb-2">
          <span class="text-xs text-[var(--td-text-color-secondary)]">方块坐标 (Block):</span>
          <span class="font-bold text-[var(--color-primary)]">X: {{ centerCoordinates.blockX }}, Z: {{ centerCoordinates.blockZ }}</span>
        </div>

        <div class="flex justify-between items-center gap-6 mb-2">
          <span class="text-xs text-[var(--td-text-color-secondary)]">区块区号 (Region):</span>
          <span class="font-bold text-[var(--color-primary)]">r.{{ centerCoordinates.regionX }}.{{ centerCoordinates.regionZ }}</span>
        </div>

        <div class="flex justify-between items-center gap-6 mb-3">
          <span class="text-xs text-[var(--td-text-color-secondary)]">当前缩放:</span>
          <span class="font-bold text-[var(--color-primary)]">{{ Math.round(scale * 100) }}%</span>
        </div>

        <div class="pt-2.5 border-t border-dashed border-zinc-200/80 dark:border-zinc-700/80 text-right text-[11px] text-[var(--td-text-color-secondary)] font-sans tracking-widest">
          🖱️ 滚轮缩放 | 按住拖拽
        </div>

      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";


.custom-grid-bg {
  background-image: linear-gradient(rgba(161, 161, 170, 0.2) 1px, transparent 1px),
  linear-gradient(90deg, rgba(161, 161, 170, 0.2) 1px, transparent 1px);
  background-size: 32px 32px;
}

/* 深色模式下的网格透明度调整 */
:global(html[theme-mode='dark']) .custom-grid-bg,
:global(html.dark) .custom-grid-bg {
  background-image: linear-gradient(rgba(82, 82, 91, 0.3) 1px, transparent 1px),
  linear-gradient(90deg, rgba(82, 82, 91, 0.3) 1px, transparent 1px);
}
</style>
