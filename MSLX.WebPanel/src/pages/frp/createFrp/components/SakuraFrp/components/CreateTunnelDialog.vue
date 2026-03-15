<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { generateRandomString } from '@/utils/tools';

interface NodeInfo {
  id: number;
  name: string;
  host: string;
  description: string;
  vip: number;
  vipName: string;
}

const props = defineProps<{
  visible: boolean;
  token: string;
  userLevel: number;
}>();

const emit = defineEmits(['update:visible', 'success']);

const loading = ref(false);
const submitting = ref(false);
const nodes = ref<NodeInfo[]>([]);

const form = reactive({
  nodeId: null as number | null,
  type: 'tcp',
  name: '',
  local_ip: '127.0.0.1',
  local_port: '25565',
  remote: '',
});

const selectedNode = computed(() => nodes.value.find((n) => n.id === form.nodeId) || null);

// 按照 VIP 等级对节点进行分组
const groupedNodes = computed(() => {
  const groupsMap = new Map<string, { label: string; value: string; children: NodeInfo[] }>();

  nodes.value.forEach((node) => {
    const groupKey = node.vipName || '默认节点';

    if (!groupsMap.has(groupKey)) {
      groupsMap.set(groupKey, { label: groupKey, value: groupKey, children: [] });
    }
    groupsMap.get(groupKey)!.children.push(node);
  });

  return Array.from(groupsMap.values());
});

const generateRandomData = () => {
  form.name = 'MSL_' + generateRandomString(6);
  form.remote = (Math.floor(Math.random() * (65535 - 10000 + 1)) + 10000).toString();
};

const fetchNodes = async () => {
  loading.value = true;
  try {
    const res = await request.get(
      {
        url: `/nodes?token=${props.token}`,
        baseURL: 'https://api.natfrp.com/v4',
      },
      { withToken: false },
    );

    if (res) {
      // 将对象的 key 转换为 id，过滤符合该用户的 VIP 等级限制
      const parsedNodes: NodeInfo[] = [];
      Object.entries(res).forEach(([idStr, nodeData]: [string, any]) => {
        if (props.userLevel >= nodeData.vip) {
          parsedNodes.push({
            id: parseInt(idStr),
            name: nodeData.name,
            host: nodeData.host,
            description: nodeData.description,
            vip: nodeData.vip,
            vipName: nodeData.vip === 0 ? "普通节点" : (nodeData.vip === 3 ? "青铜节点" : "白银节点"),
          });
        }
      });
      nodes.value = parsedNodes;

      if (props.visible && nodes.value.length > 0 && !form.nodeId) {
        form.nodeId = nodes.value[0].id;
        generateRandomData();
      }
    }
  } catch (e: any) {
    MessagePlugin.error('加载节点失败: ' + e.message);
  } finally {
    loading.value = false;
  }
};

watch(
  () => props.visible,
  (val) => {
    if (val) {
      if (nodes.value.length > 0) {
        if (!form.nodeId) form.nodeId = nodes.value[0].id;
        generateRandomData();
      } else {
        fetchNodes();
      }
    }
  },
);

const handleConfirm = async () => {
  if (!form.nodeId) {
    MessagePlugin.warning('请选择一个节点');
    return;
  }
  if (!form.name || !form.local_ip || !form.local_port) {
    MessagePlugin.warning('请填写完整的映射配置');
    return;
  }

  submitting.value = true;
  try {
    await request.post(
      {
        url: '/tunnels',
        baseURL: 'https://api.natfrp.com/v4',
        headers: { Authorization: `Bearer ${props.token}`,'Content-Type': 'application/x-www-form-urlencoded' },
        data: new URLSearchParams({
          node: form.nodeId.toString(),
          name: form.name,
          type: form.type,
          note: 'Create By MSLX',
          extra: '',
          local_ip: form.local_ip,
          local_port: form.local_port,
          remote: form.remote,
        }).toString(),
      },
      { withToken: false },
    );

    MessagePlugin.success(`隧道 ${form.name} 创建成功！`);
    emit('success');
    emit('update:visible', false);
  } catch (e: any) {
    const errorMsg = e.response?.data?.msg || e.msg || '请尝试更换隧道名称/节点！';

    MessagePlugin.error(`创建失败: ${errorMsg}`);
  } finally {
    submitting.value = false;
  }
};

onMounted(() => {
  if (props.token) fetchNodes();
});
</script>

<template>
  <t-dialog
    attach="body"
    :visible="visible"
    header="新建 SakuraFrp 隧道"
    width="580px"
    :confirm-btn="{ content: '提交创建', loading: submitting }"
    @confirm="handleConfirm"
    @close="emit('update:visible', false)"
  >
    <t-loading :loading="loading">
      <t-form
        :data="form"
        label-align="right"
        :label-width="100"
        class="pt-2.5 overflow-x-hidden [&_.t-form__item]:!mb-[22px]"
      >
        <t-form-item label="选择节点" name="nodeId">
          <t-select v-model="form.nodeId" placeholder="请选择节点" @change="generateRandomData" :popup-props="{ overlayClassName: 'max-h-[300px]' }">
            <t-option-group v-for="group in groupedNodes" :key="group.value" :label="group.label">
              <t-option v-for="node in group.children" :key="node.id" :value="node.id" :label="node.name">
                <div class="flex justify-between items-center w-full">
                  <span class="truncate">{{ node.name }}</span>
                </div>
              </t-option>
            </t-option-group>
          </t-select>
        </t-form-item>

        <t-form-item v-if="selectedNode" label="节点详情">
          <div class="w-full flex flex-col gap-2.5">
            <div class="bg-[var(--td-bg-color-secondarycontainer)] rounded-[var(--td-radius-medium)] p-3 border border-dashed border-[var(--td-component-border)]">
              <pre class="m-0 whitespace-pre-wrap break-all text-[13px] text-[var(--td-text-color-primary)] leading-[1.6]">{{ selectedNode.description || '此节点暂无备注' }}</pre>
            </div>
          </div>
        </t-form-item>

        <t-form-item label="隧道类型">
          <t-select v-model="form.type">
            <t-option label="TCP" value="tcp" />
            <t-option label="UDP" value="udp" />
            <t-option label="HTTP" value="http" />
            <t-option label="HTTPS" value="https" />
          </t-select>
        </t-form-item>

        <t-row :gutter="[16, 20]">
          <t-col :xs="12" :sm="6">
            <t-form-item label="隧道名称">
              <t-input v-model="form.name" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="远程端口">
              <t-input v-model="form.remote" placeholder="留空由服务端分配">
                <template #suffix>
                  <t-button variant="text" size="small" @click="generateRandomData">随机</t-button>
                </template>
              </t-input>
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地IP">
              <t-input v-model="form.local_ip" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地端口">
              <t-input v-model="form.local_port" />
            </t-form-item>
          </t-col>
        </t-row>

      </t-form>
    </t-loading>
  </t-dialog>
</template>

<style scoped>
</style>
