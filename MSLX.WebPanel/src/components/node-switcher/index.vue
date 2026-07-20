<script lang="ts" setup>
import { onMounted, ref, watch, computed } from 'vue';
import { useNodeStore } from '@/store';
import { request } from '@/utils/request';

const props = defineProps({
  width: {
    type: String,
    default: '150px'
  }
});

const emit = defineEmits(['change']);

const nodeStore = useNodeStore();
const activeNodeId = computed({
  get: () => nodeStore.activeNodeId,
  set: (val) => nodeStore.setActiveNode(val)
});
const nodeOptions = ref<any[]>([{ label: '主节点', value: 'local', status: 'online' }]);

const checkNodeStatus = async (url: string, nodeId: string) => {
  try {
    const controller = new AbortController();
    const id = setTimeout(() => controller.abort(), 3000);
    await request.get({
      url: '/api/status',
      baseURL: url,
      signal: controller.signal,
      headers: {
        'x-node-id': nodeId
      }
    }, { requestToSlaveNode: true });
    clearTimeout(id);
    return true;
    // eslint-disable-next-line no-unused-vars,@typescript-eslint/no-unused-vars
  } catch (e) {
    return false;
  }
};

const initNodes = async (nodes: any[]) => {
  try {
      const options: any[] = [
        { label: '主节点', value: 'local', status: 'online', disabled: false }
      ];

      // 填充已知节点，默认在线
      nodeOptions.value = [
        ...options,
        ...nodes.map((n: any) => ({
          label: n.nodeName,
          value: n.nodeId,
          url: n.nodeUrl,
          status: 'online',
          disabled: false,
          tags: n.nodeTags || n.NodeTags || ''
        }))
      ];

      let isCurrentNodeOffline = false;

      // 异步检测每个节点的状态
      const promises = nodes.map(async (n: any) => {
        const isOnline = await checkNodeStatus(n.nodeUrl, n.nodeId);

        if (!isOnline && activeNodeId.value === n.nodeId) {
          isCurrentNodeOffline = true;
        }

        return {
          label: n.nodeName,
          value: n.nodeId,
          url: n.nodeUrl,
          status: isOnline ? 'online' : 'offline',
          disabled: !isOnline,
          tags: n.nodeTags || n.NodeTags || ''
        };
      });

      const checkedNodes = await Promise.all(promises);
      nodeOptions.value = [...options, ...checkedNodes];

      if (isCurrentNodeOffline) {
        handleNodeChange('local');
      }
  } catch (e) {
    console.warn('获取节点列表失败', e);
  }
};

const handleNodeChange = (val: string) => {
  nodeStore.setActiveNode(val);
  emit('change', val);
};

onMounted(() => {
  // 初次加载
  nodeStore.fetchNodes();
});

watch(
  () => nodeStore.slaveNodes,
  (newVal, oldVal) => {
    if (JSON.stringify(newVal) !== JSON.stringify(oldVal)) {
      initNodes(newVal);
    }
  },
  { deep: true, immediate: true }
);
</script>

<template>
  <t-select
    v-if="nodeStore.slaveNodes && nodeStore.slaveNodes.length > 0"
    v-model="activeNodeId"
    class="cursor-pointer"
    placeholder="选择节点"
    :style="{ width: props.width }"
    @change="handleNodeChange"
  >
    <t-option
      v-for="option in nodeOptions"
      :key="option.value"
      :value="option.value"
      :label="option.label"
      :disabled="option.disabled"
    >
      <div class="flex items-center gap-2 w-full">
        <span
          class="w-2 h-2 rounded-full flex-shrink-0"
          :class="option.status === 'online' ? 'bg-emerald-500' : 'bg-amber-400'"
        ></span>
        <span :class="option.status === 'offline' ? 'text-zinc-400' : ''">{{ option.label }}</span>
        <template v-if="option.tags">
          <t-tag
            v-for="(tag, idx) in option.tags.split(/[，,]/).map((t: string) => t.trim()).filter(Boolean)"
            :key="tag"
            size="small"
            variant="light"
            :theme="['primary', 'success', 'warning', 'danger'][idx % 4] as any"
            class="scale-90 origin-left"
          >
            {{ tag }}
          </t-tag>
        </template>
        <span v-if="option.status === 'offline'" class="text-[10px] text-amber-500/80 ml-auto">(离线)</span>
      </div>
    </t-option>

    <template #valueDisplay>
      <div class="flex items-center gap-1.5 cursor-pointer w-full overflow-hidden">
        <span
          class="w-2 h-2 rounded-full flex-shrink-0"
          :class="(nodeOptions.find(n => n.value === activeNodeId)?.status || 'online') === 'online' ? 'bg-emerald-500' : 'bg-amber-400'"
        ></span>
        <span class="truncate">{{ nodeOptions.find(n => n.value === activeNodeId)?.label || '选择节点' }}</span>
      </div>
    </template>
  </t-select>
</template>
