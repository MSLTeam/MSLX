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
      class="map-viewport"
      @wheel="handleScroll"
      @mousedown="startDrag"
      @mousemove="onDrag"
      @mouseup="stopDrag"
      @mouseleave="stopDrag"
    >
      <div class="map-layer" :style="{ transform: `translate(${translateX}px, ${translateY}px) scale(${scale})` }">
        <div
          v-for="tile in loadedRegions"
          :key="tile.key"
          class="tile-wrapper"
          :style="{ left: `${tile.x * 512}px`, top: `${tile.z * 512}px` }"
        >
          <img
            :src="tile.src"
            class="pixel-map-tile"
            :class="{ 'is-loaded': tile.loaded }"
            @load="tile.loaded = true"
            alt="tile"
            draggable="false"
          />
        </div>
      </div>

      <div class="center-crosshair"></div>

      <div class="search-panel" @mousedown.stop @wheel.stop>
        <t-space align="center" size="small">
          <t-input v-model="inputX" type="number" placeholder="X 坐标" style="width: 100px" @enter="handleSearchJump" />
          <t-input v-model="inputZ" type="number" placeholder="Z 坐标" style="width: 100px" @enter="handleSearchJump" />
          <t-button theme="primary" @click="handleSearchJump">
            <template #icon><location-icon /></template> 定位
          </t-button>
          <t-button variant="outline" theme="default" @click="jumpToSpawn" title="回到世界出生点">
            <template #icon><home-icon /></template>
          </t-button>
        </t-space>
      </div>

      <div class="map-controls">
        <div class="coord-item">
          <span class="label">方块坐标 (Block):</span>
          <span class="value">X: {{ centerCoordinates.blockX }}, Z: {{ centerCoordinates.blockZ }}</span>
        </div>
        <div class="coord-item">
          <span class="label">区块区号 (Region):</span>
          <span class="value">r.{{ centerCoordinates.regionX }}.{{ centerCoordinates.regionZ }}</span>
        </div>
        <div class="coord-item">
          <span class="label">当前缩放:</span>
          <span class="value">{{ Math.round(scale * 100) }}%</span>
        </div>
        <div class="helper-text">🖱️ 滚轮缩放 | 按住拖拽</div>
      </div>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.map-viewport {
  width: 100%;
  height: 65vh;
  min-height: 500px;
  background-color: var(--td-bg-color-page);
  background-image:
    linear-gradient(var(--td-component-stroke) 1px, transparent 1px),
    linear-gradient(90deg, var(--td-component-stroke) 1px, transparent 1px);
  background-size: 32px 32px;
  overflow: hidden;
  position: relative;
  cursor: grab;
  border-radius: var(--td-radius-default);
  border: 1px solid var(--td-component-stroke);
  user-select: none;
  -webkit-user-select: none;
}

.map-viewport:active {
  cursor: grabbing;
}

.map-layer {
  position: absolute;
  top: 50%;
  left: 50%;
  transform-origin: center;
  transition: transform 0.05s linear;
}

.tile-wrapper {
  position: absolute;
  width: 512px;
  height: 512px;
  margin-left: -256px;
  margin-top: -256px;
  display: flex;
  align-items: center;
  justify-content: center;
  background-color: var(--td-bg-color-container);
}

.pixel-map-tile {
  width: 100%;
  height: 100%;
  image-rendering: pixelated;
  pointer-events: none;
  opacity: 0;
  transition: opacity 0.5s ease-out;
}
.pixel-map-tile.is-loaded {
  opacity: 1;
}

/* ================= 屏幕中心准星 ================= */
.center-crosshair {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 14px;
  height: 14px;
  margin-top: -7px;
  margin-left: -7px;
  pointer-events: none;
  z-index: 10;

  &::before,
  &::after {
    content: '';
    position: absolute;
    background: rgba(255, 60, 60, 0.8);
    box-shadow: 0 0 2px rgba(0, 0, 0, 0.5);
  }
  &::before {
    left: 6px;
    top: 0;
    width: 2px;
    height: 14px;
  }
  &::after {
    left: 0;
    top: 6px;
    width: 14px;
    height: 2px;
  }
}

.search-panel {
  position: absolute;
  top: 16px;
  left: 16px;
  padding: 12px;
  background: var(--td-bg-color-container);
  border-radius: var(--td-radius-default);
  border: 1px solid var(--td-component-stroke);
  box-shadow: var(--td-shadow-2);
  z-index: 20;
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  background-color: rgba(var(--td-bg-color-container-rgb), 0.85);
  cursor: default;
}

.map-controls {
  position: absolute;
  bottom: 16px;
  right: 16px;
  background: var(--td-bg-color-container);
  color: var(--td-text-color-primary);
  padding: 12px 16px;
  border-radius: var(--td-radius-default);
  font-size: 13px;
  font-family: monospace;
  pointer-events: none;
  border: 1px solid var(--td-component-stroke);
  box-shadow: var(--td-shadow-2);
  z-index: 20;
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  background-color: rgba(var(--td-bg-color-container-rgb), 0.85);

  .coord-item {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    margin-bottom: 6px;

    .label {
      color: var(--td-text-color-secondary);
    }
    .value {
      font-weight: bold;
      color: var(--td-brand-color);
    }
  }

  .helper-text {
    margin-top: 12px;
    padding-top: 8px;
    border-top: 1px solid var(--td-component-stroke);
    color: var(--td-text-color-placeholder);
    text-align: right;
    font-size: 12px;
  }
}
</style>
