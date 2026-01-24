<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { generateRandomString } from '@/utils/tools';
import { changeUrl } from '@/router';

interface NodeInfo {
  id: number;
  node: string;
  domain: string;
  ip: string;
  port: number;
  min_open_port: number;
  max_open_port: number;
  udp_support: number;
  http_support: number;
  kcp_support: number;
  need_real_name: number;
  bandwidth: number;
  allow_user_group: number;
  remarks: string;
  status: number;
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
  type: 'tcp',
  localIP: '127.0.0.1',
  localPort: '25565',
  remotePort: '',
  name: '',
  remarks: '无',
  bindDomain: '',
  use_kcp: false,
  extra_config: '',
});

const selectedNode = computed(() => nodes.value.find((n) => n.id === form.nodeId) || null);

const groupedNodes = computed(() => {
  const groups = [
    { label: '免费节点', value: 0, children: [] as NodeInfo[] },
    { label: '高级节点', value: 1, children: [] as NodeInfo[] },
    { label: '超级节点', value: 2, children: [] as NodeInfo[] },
  ];
  nodes.value.forEach((node) => {
    const group = groups.find((g) => g.value === node.allow_user_group);
    if (group) group.children.push(node);
  });
  return groups.filter((g) => g.children.length > 0);
});

const generateRandomData = () => {
  if (selectedNode.value) {
    const { min_open_port, max_open_port } = selectedNode.value;
    form.remotePort = (Math.floor(Math.random() * (max_open_port - min_open_port + 1)) + min_open_port).toString();
  }
  form.name = generateRandomString(8);
};

const fetchNodes = async () => {
  loading.value = true;
  try {
    const res = await request.get({
      url: '/api/frp/nodeList',
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${props.token}` },
    });
    if (res.code === 200) {
      nodes.value = res.data;
      if (props.visible && nodes.value.length > 0 && !form.nodeId) {
        form.nodeId = nodes.value[0].id;
        generateRandomData();
      }
    }
  } catch (e: any) {
    MessagePlugin.error('加载节点失败' + e.message);
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
  if (!form.nodeId) return;
  submitting.value = true;
  try {
    const res = await request.post({
      url: '/api/frp/addTunnel',
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${props.token}` },
      data: {
        id: form.nodeId,
        type: form.type,
        local_ip: form.localIP,
        local_port: form.localPort,
        remote_port: form.remotePort,
        name: form.name,
        remarks: form.remarks,
        bind_domain: form.bindDomain,
        use_kcp: selectedNode.value?.kcp_support === 1 ? form.use_kcp : false,
        extra_config: form.extra_config || null,
      },
    });
    if (res.code === 200) {
      MessagePlugin.success('创建成功');
      emit('success');
      emit('update:visible', false);
    } else {
      MessagePlugin.error(res.msg);
    }
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
    header="新建隧道"
    width="580px"
    :confirm-btn="{ content: '提交创建', loading: submitting }"
    @confirm="handleConfirm"
    @close="emit('update:visible', false)"
  >
    <t-form :data="form" label-align="right" :label-width="100" class="dialog-inner-form">
      <t-form-item label="选择节点" name="nodeId">
        <t-row :gutter="8" style="width: 100%">
          <t-col flex="auto">
            <t-select v-model="form.nodeId" placeholder="请选择节点" @change="generateRandomData">
              <t-option-group v-for="group in groupedNodes" :key="group.value" :label="group.label">
                <t-option v-for="node in group.children" :key="node.id" :value="node.id" :label="node.node">
                  <div class="custom-option-item">
                    <span class="node-name-text">{{ node.node }}</span>
                    <div class="node-tag-group">
                      <t-tag size="small" variant="outline" theme="primary">{{ node.bandwidth }}M</t-tag>
                      <t-tag size="small" :theme="node.status === 1 ? 'success' : 'danger'">
                        {{ node.status === 1 ? '在线' : '离线' }}
                      </t-tag>
                    </div>
                  </div>
                </t-option>
              </t-option-group>
            </t-select>
          </t-col>
          <t-col flex="none">
            <t-button variant="outline" @click="changeUrl('https://user.mslmc.net/frp/createTunnel')"> 前往源站创建 </t-button>
          </t-col>
        </t-row>
      </t-form-item>

      <t-form-item v-if="selectedNode" label="节点详情">
        <div class="node-detail-area">
          <div class="detail-tags-row">
            <t-tag size="small" variant="outline" theme="primary">{{ selectedNode.bandwidth }}Mbps</t-tag>
            <t-tag size="small" :theme="selectedNode.need_real_name ? 'success' : 'warning'">
              {{ selectedNode.need_real_name ? '需要实名认证' : '无需实名认证' }}
            </t-tag>
            <t-tag size="small" :theme="selectedNode.status === 1 ? 'success' : 'danger'">
              节点状态：{{ selectedNode.status === 1 ? '在线' : '离线' }}
            </t-tag>
          </div>
          <div class="remarks-box">
            <pre>{{ selectedNode.remarks || '此节点暂无备注' }}</pre>
          </div>
        </div>
      </t-form-item>

      <t-form-item label="隧道类型">
        <t-select v-model="form.type">
          <t-option label="TCP" value="tcp" />
          <t-option v-if="selectedNode?.udp_support" label="UDP" value="udp" />
          <t-option v-if="selectedNode?.http_support" label="HTTP" value="http" />
          <t-option v-if="selectedNode?.http_support" label="HTTPS" value="https" />
        </t-select>
      </t-form-item>

      <t-row :gutter="[16, 20]">
        <t-col :span="6">
          <t-form-item label="隧道名称">
            <t-input v-model="form.name" />
          </t-form-item>
        </t-col>
        <t-col :span="6">
          <t-form-item label="远程端口">
            <t-input v-model="form.remotePort">
              <template #suffix>
                <t-button variant="text" size="small" @click="generateRandomData">随机</t-button>
              </template>
            </t-input>
          </t-form-item>
        </t-col>
        <t-col :span="6">
          <t-form-item label="本地IP">
            <t-input v-model="form.localIP" />
          </t-form-item>
        </t-col>
        <t-col :span="6">
          <t-form-item label="本地端口">
            <t-input v-model="form.localPort" />
          </t-form-item>
        </t-col>
      </t-row>

      <t-form-item v-if="form.type.includes('http')" label="绑定域名" class="spacing-item">
        <t-input v-model="form.bindDomain" placeholder="输入已解析的域名" />
      </t-form-item>

      <t-form-item label="备注说明" class="spacing-item">
        <t-input v-model="form.remarks" />
      </t-form-item>

      <t-form-item label="额外参数" class="spacing-item">
        <t-textarea v-model="form.extra_config" :autosize="{ minRows: 2 }" placeholder="选填，高级配置参数（不懂清留空！！！）" />
      </t-form-item>
    </t-form>
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

/* 详情展示区样式 */
.node-detail-area {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.detail-tags-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
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
