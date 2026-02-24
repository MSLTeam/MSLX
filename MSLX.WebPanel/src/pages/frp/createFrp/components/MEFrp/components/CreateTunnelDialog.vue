<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { generateRandomString } from '@/utils/tools';

interface NodeInfo {
  nodeId: number;
  name: string;
  hostname: string;
  description: string;
  allowGroup: string;
  allowPort: string;
  allowType: string;
  region: string;
  bandwidth: string;
  isOnline: boolean;
  isDisabled: boolean;
}

const props = defineProps<{
  visible: boolean;
  token: string;
}>();

const emit = defineEmits(['update:visible', 'success']);

const loading = ref(false);
const submitting = ref(false);
const nodes = ref<NodeInfo[]>([]);

const form = reactive({
  nodeId: null as number | null,
  proxyType: 'tcp',
  localIp: '127.0.0.1',
  localPort: '25565',
  remotePort: '',
  proxyName: '',
  domain: '',
});

const selectedNode = computed(() => nodes.value.find((n) => n.nodeId === form.nodeId) || null);

const groupedNodes = computed(() => {
  const groupsMap = new Map<string, { label: string; value: string; children: NodeInfo[] }>();

  nodes.value.forEach((node) => {
    const regionKey = node.region || 'unknown';

    if (!groupsMap.has(regionKey)) {
      let label = regionKey;
      if (regionKey === 'oversea') label = '海外节点';
      else if (regionKey === 'cn') label = '国内节点';
      else label = '默认节点';

      groupsMap.set(regionKey, { label, value: regionKey, children: [] });
    }
    groupsMap.get(regionKey)!.children.push(node);
  });

  return Array.from(groupsMap.values());
});

const generateRandomData = () => {
  form.remotePort = (Math.floor(Math.random() * (65535 - 10000 + 1)) + 10000).toString();
  form.proxyName = 'MSLX_' + generateRandomString(6);
};

const fetchNodes = async () => {
  loading.value = true;
  try {
    const res = await request.get(
      {
        url: '/auth/node/list',
        baseURL: 'https://api.mefrp.com/api',
        headers: { Authorization: `Bearer ${props.token}` },
      },
      { withToken: false },
    );

    if (res) {
      // 过滤掉不可用的节点
      nodes.value = res.filter((n: NodeInfo) => !n.isDisabled);

      if (props.visible && nodes.value.length > 0 && !form.nodeId) {
        form.nodeId = nodes.value[0].nodeId;
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
        if (!form.nodeId) form.nodeId = nodes.value[0].nodeId;
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
  submitting.value = true;
  try {
    await request.post(
      {
        url: '/auth/proxy/create',
        baseURL: 'https://api.mefrp.com/api',
        headers: { Authorization: `Bearer ${props.token}` },
        data: {
          accessKey: '',
          headerXFromWhere: '',
          hostHeaderRewrite: '',
          proxyProtocolVersion: '',
          nodeId: form.nodeId,
          proxyName: form.proxyName,
          proxyType: form.proxyType,
          localIp: form.localIp,
          localPort: parseInt(form.localPort),
          remotePort: parseInt(form.remotePort),
          domain: form.domain,
          useCompression: false,
          useEncryption: false,
        },
      },
      { withToken: false },
    );

    MessagePlugin.success(`隧道 ${form.proxyName} 创建成功！`);
    emit('success');
    emit('update:visible', false);
  } catch (e: any) {
    MessagePlugin.error('创建异常: ' + e.message);
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
    header="新建 ME Frp 隧道"
    width="580px"
    :confirm-btn="{ content: '提交创建', loading: submitting }"
    @confirm="handleConfirm"
    @close="emit('update:visible', false)"
  >
    <t-loading :loading="loading">
      <t-form :data="form" label-align="right" :label-width="100" class="dialog-inner-form">
        <t-form-item label="选择节点" name="nodeId">
          <t-select v-model="form.nodeId" placeholder="请选择节点" @change="generateRandomData">
            <t-option-group v-for="group in groupedNodes" :key="group.value" :label="group.label">
              <t-option v-for="node in group.children" :key="node.nodeId" :value="node.nodeId" :label="node.name">
                <div class="custom-option-item">
                  <span class="node-name-text">{{ node.name }}</span>
                  <div class="node-tag-group">
                    <t-tag size="small" variant="outline" theme="primary">{{ node.bandwidth || '未知带宽' }}</t-tag>
                    <t-tag size="small" :theme="node.isOnline ? 'success' : 'danger'">{{
                      node.isOnline ? '在线' : '离线'
                    }}</t-tag>
                  </div>
                </div>
              </t-option>
            </t-option-group>
          </t-select>
        </t-form-item>

        <t-form-item v-if="selectedNode" label="节点详情">
          <div class="node-detail-area">
            <div class="remarks-box">
              <pre>{{ selectedNode.description || '此节点暂无备注' }}</pre>
            </div>
          </div>
        </t-form-item>

        <t-form-item label="隧道类型">
          <t-select v-model="form.proxyType">
            <t-option v-if="selectedNode?.allowType.includes('tcp')" label="TCP" value="tcp" />
            <t-option v-if="selectedNode?.allowType.includes('udp')" label="UDP" value="udp" />
            <t-option v-if="selectedNode?.allowType.includes('http')" label="HTTP" value="http" />
            <t-option v-if="selectedNode?.allowType.includes('https')" label="HTTPS" value="https" />
          </t-select>
        </t-form-item>

        <t-row :gutter="[16, 20]">
          <t-col :xs="12" :sm="6">
            <t-form-item label="隧道名称">
              <t-input v-model="form.proxyName" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="远程端口">
              <t-input v-model="form.remotePort">
                <template #suffix>
                  <t-button variant="text" size="small" @click="generateRandomData">随机</t-button>
                </template>
              </t-input>
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地IP">
              <t-input v-model="form.localIp" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地端口">
              <t-input v-model="form.localPort" />
            </t-form-item>
          </t-col>
        </t-row>

        <t-form-item v-if="form.proxyType.includes('http')" label="绑定域名" class="spacing-item">
          <t-input v-model="form.domain" placeholder="输入绑定的域名" />
        </t-form-item>
      </t-form>
    </t-loading>
  </t-dialog>
</template>

<style scoped>
.dialog-inner-form {
  padding-top: 10px;
  overflow-x: hidden;
}
:deep(.t-form__item) {
  margin-bottom: 22px !important;
}
.spacing-item {
  margin-top: 4px;
}
.custom-option-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}
.node-name-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.node-tag-group {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
  margin-left: 12px;
}
.node-detail-area {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.remarks-box {
  background: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);
  padding: 12px;
  border: 1px dashed var(--td-component-border);
}
.remarks-box pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
  font-size: 13px;
  color: var(--td-text-color-primary);
  line-height: 1.6;
}
</style>
