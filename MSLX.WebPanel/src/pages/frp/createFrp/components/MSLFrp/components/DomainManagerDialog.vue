<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import { AddIcon, EditIcon, DeleteIcon, RollbackIcon, InternetIcon, LinkIcon } from 'tdesign-icons-vue-next';
import { request } from '@/utils/request';
import { changeUrl } from '@/router';

interface Props {
  visible: boolean;
  token: string;
  tunnels: any[];
}

const props = defineProps<Props>();
const emit = defineEmits(['update:visible']);

interface DnsRecord {
  id: number;
  domain_id: number;
  domain: string;
  name: string;
  record: string;
  type: string;
}

interface DomainPool {
  id: number;
  domain: string;
  allow_types: string;
  remark: string;
}

interface NodeInfo {
  id: number;
  node: string;
  ip: string;
  domain: string;
  [key: string]: any;
}

const mode = ref<'list' | 'form'>('list');
const formMode = ref<'mc_srv' | 'custom'>('mc_srv');
const loading = ref(false);
const submitting = ref(false);

const records = ref<DnsRecord[]>([]);
const domainPool = ref<DomainPool[]>([]);
const internalNodeList = ref<NodeInfo[]>([]);
const selectedTunnelId = ref<number | null>(null);

const formData = reactive({
  id: 0,
  domain_id: undefined as number | undefined,
  name: '',
  type: 'A',
  record: '',
});

const tcpTunnels = computed(() => {
  if (!props.tunnels) return [];
  return props.tunnels.filter((t) => t.type && t.type.toUpperCase() === 'TCP');
});

const currentDomainRemark = computed(() => {
  const d = domainPool.value.find((item) => item.id === formData.domain_id);
  return d ? d.remark : '';
});

const initData = async () => {
  if (!props.token) return;
  loading.value = true;
  try {
    await Promise.all([fetchRecords(), fetchDomainPool(), fetchNodes()]);
  } catch (e) {
    console.error(e);
  } finally {
    loading.value = false;
  }
};

const fetchRecords = async () => {
  const res = await request.get({
    url: '/api/domain/dns/list',
    baseURL: 'https://user.mslmc.net',
    headers: { Authorization: `Bearer ${props.token}` },
  });
  if (res.code === 200) records.value = res.data || [];
};

const fetchDomainPool = async () => {
  const res = await request.get({
    url: '/api/domain/list',
    baseURL: 'https://user.mslmc.net',
    headers: { Authorization: `Bearer ${props.token}` },
  });
  if (res.code === 200) domainPool.value = res.data || [];
};

const fetchNodes = async () => {
  try {
    const res = await request.get({
      url: '/api/frp/nodeList',
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${props.token}` },
    });
    if (res.code === 200) {
      internalNodeList.value = res.data || [];
    }
  } catch (e) {
    console.error('Fetch nodes failed', e);
  }
};

const handleTunnelSelect = (val: number) => {
  const tunnel = props.tunnels.find((t) => t.id === val);

  if (tunnel) {
    const node = internalNodeList.value.find((n) => n.id === tunnel.node_id);

    const targetAddress = node ? node.domain || node.ip : null;

    if (!targetAddress) {
      MessagePlugin.warning(`无法获取节点(ID:${tunnel.node_id})地址，请手动填写`);
      formData.record = `5 5 ${tunnel.remote_port} 请输入节点地址`;
    } else {
      formData.record = `5 5 ${tunnel.remote_port} ${targetAddress}`;
      MessagePlugin.success('已自动生成解析值');
    }

    formData.type = 'SRV';
  }
};

onMounted(() => {
  if (props.visible && props.token) initData();
});

watch(
  () => props.visible,
  (val) => {
    if (val) {
      mode.value = 'list';
      initData();
    }
  },
);

watch(domainPool, (newVal) => {
  if (newVal.length > 0 && formData.domain_id === undefined && formData.id === 0) {
    formData.domain_id = newVal[0].id;
  }
});

const getFriendlySrvAddr = (item: DnsRecord) => {
  if (item.type !== 'SRV') return '';
  const cleanName = item.name.replace('_minecraft._tcp.', '');
  return `${cleanName}.${item.domain}`;
};

const goToAdd = () => {
  mode.value = 'form';
  formMode.value = 'mc_srv';
  selectedTunnelId.value = null;

  formData.id = 0;
  formData.name = '';
  formData.type = 'SRV';
  formData.record = '';

  if (domainPool.value.length > 0) {
    formData.domain_id = domainPool.value[0].id;
  }
};

const goToEdit = (item: DnsRecord) => {
  mode.value = 'form';
  formMode.value = 'custom';

  formData.id = item.id;
  formData.domain_id = Number(item.domain_id);
  formData.name = item.name;
  formData.type = item.type;
  formData.record = item.record;
};

const goBack = () => {
  mode.value = 'list';
};

const handleSubmit = async () => {
  const isSRV = formData.type.toUpperCase() === 'SRV' || formMode.value === 'mc_srv';
  const nameRegex = isSRV ? /^[a-zA-Z0-9._-]+$/ : /^[a-zA-Z0-9]+$/;

  let finalName = formData.name;

  if (finalName.length < 1) return MessagePlugin.warning('请输入子域名名称');

  if (!nameRegex.test(finalName)) {
    return MessagePlugin.warning(isSRV ? 'SRV支持英文、数字、下划线及点' : '普通解析仅支持英文数字');
  }

  if (formMode.value === 'mc_srv' && formData.id === 0) {
    if (!finalName.startsWith('_minecraft._tcp.')) {
      const prefix = '_minecraft._tcp.';
      finalName = prefix + finalName;
    }
    formData.type = 'SRV';
  }

  submitting.value = true;
  const url = formData.id !== 0 ? '/api/domain/dns/edit' : '/api/domain/dns/add';

  try {
    const res = await request.post({
      url,
      baseURL: 'https://user.mslmc.net',
      headers: { Authorization: `Bearer ${props.token}` },
      data: { ...formData, name: finalName },
    });

    if (res.code === 200) {
      MessagePlugin.success(res.msg || '操作成功');
      await fetchRecords();
      mode.value = 'list';
    } else {
      MessagePlugin.error(res.msg);
    }
  } catch (e: any) {
    MessagePlugin.error('请求失败: ' + e.message);
  } finally {
    submitting.value = false;
  }
};

const handleDelete = async (id: number) => {
  const confirmDia = DialogPlugin.confirm({
    header: '确认删除',
    body: '确定要删除这条解析记录吗？删除后无法恢复。',
    onConfirm: async () => {
      confirmDia.hide();
      try {
        const res = await request.post({
          url: '/api/domain/dns/delete',
          baseURL: 'https://user.mslmc.net',
          headers: { Authorization: `Bearer ${props.token}` },
          data: { id },
        });
        if (res.code === 200) {
          MessagePlugin.success('删除成功');
          fetchRecords();
        } else {
          MessagePlugin.error(res.msg);
        }
      } catch (e: any) {
        MessagePlugin.error(e.message);
      }
    },
  });
};

const closeDialog = () => {
  emit('update:visible', false);
};
</script>

<template>
  <t-dialog
    :visible="visible"
    :header="mode === 'list' ? 'MSLFrp免费子域名管理' : formData.id === 0 ? '创建新解析' : '编辑解析'"
    width="650px"
    attach="body"
    :footer="false"
    @close="closeDialog"
  >
    <div v-if="mode === 'list'" class="manager-container">
      <div class="toolbar">
        <t-button variant="text" theme="default" @click="changeUrl('https://www.mslmc.cn/docs/proxy/server-no-port/')">
          <template #icon><internet-icon /></template>查看文档
        </t-button>
        <t-button theme="primary" @click="goToAdd">
          <template #icon><add-icon /></template>新建解析
        </t-button>
      </div>

      <t-loading :loading="loading" text="加载中...">
        <div class="record-list">
          <div v-if="records.length === 0" class="empty-state">
            <t-empty title="暂无解析记录" description="点击上方按钮创建一个吧" />
          </div>

          <div v-for="item in records" :key="item.id" class="record-card">
            <div class="card-main">
              <div class="domain-title">
                <span class="sub">{{ item.name }}</span
                ><span class="root">.{{ item.domain }}</span>
              </div>

              <div class="info-badges">
                <t-tag size="small" :theme="item.type === 'SRV' ? 'warning' : 'primary'" variant="light">
                  {{ item.type }}
                </t-tag>
                <span class="record-value" :title="item.record">{{ item.record }}</span>
              </div>

              <div v-if="item.type === 'SRV'" class="srv-helper">
                <link-icon />
                <span
                  >地址: <strong>{{ getFriendlySrvAddr(item) }}</strong></span
                >
              </div>
            </div>

            <div class="card-actions">
              <t-tooltip content="编辑">
                <t-button shape="circle" variant="text" @click="goToEdit(item)">
                  <template #icon><edit-icon /></template>
                </t-button>
              </t-tooltip>
              <t-tooltip content="删除">
                <t-button shape="circle" variant="text" theme="danger" @click="handleDelete(item.id)">
                  <template #icon><delete-icon /></template>
                </t-button>
              </t-tooltip>
            </div>
          </div>
        </div>
      </t-loading>
    </div>

    <div v-else class="form-container">
      <div class="form-header">
        <t-button variant="text" size="small" @click="goBack">
          <template #icon><rollback-icon /></template> 返回列表
        </t-button>
      </div>

      <t-tabs v-if="formData.id === 0" v-model="formMode" class="mode-tabs">
        <t-tab-panel value="mc_srv" label="MC Java版隐藏端口" />
        <t-tab-panel value="custom" label="自定义解析" />
      </t-tabs>

      <t-form label-align="top" :data="formData" class="dns-form">
        <t-form-item label="选择域名后缀">
          <t-select v-model="formData.domain_id" :disabled="formData.id !== 0" placeholder="请选择后缀" filterable>
            <t-option v-for="d in domainPool" :key="d.id" :value="d.id" :label="d.domain">
              {{ d.domain }}
            </t-option>
          </t-select>
        </t-form-item>

        <t-form-item v-if="currentDomainRemark" label="备注">
          <div class="remark-display">
            {{ currentDomainRemark }}
          </div>
        </t-form-item>

        <t-form-item label="子域名称" help="起一个你喜欢的前缀即可">
          <t-input v-model="formData.name" placeholder="例如: myserver">
            <template #prefix-icon>
              <span v-if="formMode === 'mc_srv' && formData.id === 0" class="prefix-hint">_minecraft._tcp.</span>
            </template>
          </t-input>
        </t-form-item>

        <template v-if="formMode === 'mc_srv' && formData.id === 0">
          <t-form-item label="选择隧道 (自动生成解析值)">
            <t-select v-model="selectedTunnelId" placeholder="点击选择已有的 TCP 隧道" @change="handleTunnelSelect">
              <t-option
                v-for="t in tcpTunnels"
                :key="t.id"
                :value="t.id"
                :label="`${t.name} (端口: ${t.remote_port})`"
              />
            </t-select>
          </t-form-item>
        </template>

        <div class="row-fields">
          <t-form-item label="记录类型" class="half-width">
            <t-select v-model="formData.type">
              <t-option label="A (IPv4)" value="A" />
              <t-option label="CNAME (别名)" value="CNAME" />
              <t-option label="AAAA (IPv6)" value="AAAA" />
              <t-option label="SRV (服务记录)" value="SRV" />
            </t-select>
          </t-form-item>

          <t-form-item label="解析记录值" class="half-width">
            <t-input v-model="formData.record" placeholder="例: 5 5 25565 node.mslmc.net" />
          </t-form-item>
        </div>

        <div class="form-actions">
          <t-button theme="default" variant="base" @click="goBack">取消</t-button>
          <t-button theme="primary" :loading="submitting" @click="handleSubmit">
            {{ formData.id === 0 ? '立即创建' : '保存修改' }}
          </t-button>
        </div>
      </t-form>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
.manager-container {
  min-height: 400px;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
}

.record-list {
  max-height: 500px;
  overflow-y: auto;
  padding-right: 4px;
}

.record-card {
  background: var(--td-bg-color-container);
  border: 1px solid var(--td-component-border);
  border-radius: var(--td-radius-medium);
  padding: 12px 16px;
  margin-bottom: 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  transition: all 0.2s;

  &:hover {
    border-color: var(--td-brand-color);
    background: var(--td-bg-color-secondarycontainer);
  }

  .card-main {
    flex: 1;
    overflow: hidden;

    .domain-title {
      font-size: 16px;
      font-weight: 600;
      margin-bottom: 6px;
      .sub {
        color: var(--td-brand-color);
      }
      .root {
        color: var(--td-text-color-secondary);
      }
    }

    .info-badges {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 6px;

      .record-value {
        font-family: monospace;
        font-size: 13px;
        color: var(--td-text-color-primary);
        background: var(--td-bg-color-component);
        padding: 2px 6px;
        border-radius: 4px;
        max-width: 250px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }

    .srv-helper {
      font-size: 12px;
      color: var(--td-success-color);
      display: flex;
      align-items: center;
      gap: 4px;
    }
  }

  .card-actions {
    display: flex;
    gap: 4px;
    margin-left: 8px;
  }
}

.form-container {
  padding-top: 4px;
}
.form-header {
  margin-bottom: 8px;
}
.mode-tabs {
  margin-bottom: 16px;
}
.dns-form {
  .remark-display {
    padding: 8px 12px;
    background: var(--td-bg-color-secondarycontainer);
    border-radius: var(--td-radius-small);
    font-size: 13px;
    color: var(--td-text-color-secondary);
    white-space: pre-wrap;
    word-break: break-all;
    line-height: 1.5;
    border: 1px dashed var(--td-component-border);
    width: 100%;
  }

  .prefix-hint {
    color: var(--td-text-color-secondary);
    padding: 0 4px;
    background: var(--td-bg-color-component);
    margin-right: 4px;
    border-radius: 2px;
    font-family: monospace;
    font-size: 12px;
  }

  .row-fields {
    display: flex;
    gap: 16px;
    .half-width {
      flex: 1;
    }
  }

  .form-actions {
    display: flex;
    justify-content: flex-end;
    gap: 12px;
    margin-top: 24px;
  }
}
</style>
