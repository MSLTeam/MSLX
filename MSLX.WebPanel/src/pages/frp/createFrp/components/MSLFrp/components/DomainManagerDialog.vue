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
    :header="mode === 'list' ? 'MSLFrp 免费子域名管理' : formData.id === 0 ? '创建新解析' : '编辑解析'"
    width="650px"
    attach="body"
    :footer="false"
    @close="closeDialog"
  >
    <div v-if="mode === 'list'" class="min-h-[400px] flex flex-col">
      <div class="flex justify-between items-center mb-4 shrink-0">
        <t-button variant="text" theme="default" class="!text-zinc-500 hover:!text-[var(--color-primary)]" @click="changeUrl('https://www.mslmc.cn/docs/proxy/server-no-port/')">
          <template #icon><internet-icon /></template>查看文档
        </t-button>
        <t-button theme="primary" class="!rounded-lg !font-bold shadow-sm shadow-[var(--color-primary-light)]/30" @click="goToAdd">
          <template #icon><add-icon /></template>新建解析
        </t-button>
      </div>

      <t-loading :loading="loading" text="加载中...">
        <div class="max-h-[500px] overflow-y-auto custom-scrollbar pr-1 flex flex-col gap-3">
          <div v-if="records.length === 0" class="py-10">
            <t-empty title="暂无解析记录" description="点击上方按钮创建一个吧" />
          </div>

          <div v-for="item in records" :key="item.id" class="group flex justify-between items-center bg-zinc-50/80 dark:bg-zinc-800/40 border border-zinc-200/80 dark:border-zinc-700/60 rounded-xl p-3 sm:p-4 transition-all duration-300 hover:border-[var(--color-primary)]/50 hover:bg-white dark:hover:bg-zinc-800 hover:shadow-sm">

            <div class="flex-1 overflow-hidden flex flex-col">
              <div class="text-base font-extrabold mb-1.5 truncate tracking-tight">
                <span class="text-[var(--color-primary)]">{{ item.name }}</span><span class="text-[var(--td-text-color-secondary)]">.{{ item.domain }}</span>
              </div>

              <div class="flex items-center gap-2 mb-1.5">
                <t-tag size="small" :theme="item.type === 'SRV' ? 'warning' : 'primary'" variant="light" class="!rounded !font-bold !px-1.5 border" :class="item.type === 'SRV' ? 'border-amber-500/20' : 'border-[var(--color-primary)]/20'">
                  {{ item.type }}
                </t-tag>
                <span class="font-mono text-xs sm:text-[13px] font-bold text-zinc-700 dark:text-zinc-300 bg-zinc-200/50 dark:bg-zinc-900/50 border border-zinc-200 dark:border-zinc-700 px-1.5 py-0.5 rounded-md max-w-[200px] sm:max-w-[250px] truncate" :title="item.record">{{ item.record }}</span>
              </div>

              <div v-if="item.type === 'SRV'" class="text-xs text-[var(--color-success)] flex items-center gap-1 mt-0.5 truncate font-medium">
                <link-icon class="shrink-0" />
                <span class="truncate">地址: <strong class="font-mono font-extrabold">{{ getFriendlySrvAddr(item) }}</strong></span>
              </div>
            </div>

            <div class="flex gap-1 ml-3 shrink-0 opacity-80 group-hover:opacity-100 transition-opacity">
              <t-tooltip content="编辑">
                <t-button shape="circle" variant="text" class="hover:!bg-zinc-200 dark:hover:!bg-zinc-700" @click="goToEdit(item)">
                  <template #icon><edit-icon class="text-zinc-600 dark:text-zinc-300" /></template>
                </t-button>
              </t-tooltip>
              <t-tooltip content="删除">
                <t-button shape="circle" variant="text" theme="danger" class="hover:!bg-red-500/10" @click="handleDelete(item.id)">
                  <template #icon><delete-icon /></template>
                </t-button>
              </t-tooltip>
            </div>
          </div>
        </div>
      </t-loading>
    </div>

    <div v-else class="pt-1 flex flex-col">
      <div class="mb-2 shrink-0">
        <t-button variant="text" size="small" class="!text-zinc-500 hover:!text-[var(--color-primary)] !rounded-md" @click="goBack">
          <template #icon><rollback-icon /></template> 返回列表
        </t-button>
      </div>

      <t-tabs v-if="formData.id === 0" v-model="formMode" class="!mb-4">
        <t-tab-panel value="mc_srv" label="MC Java版隐藏端口" />
        <t-tab-panel value="custom" label="自定义解析" />
      </t-tabs>

      <t-form label-align="top" :data="formData" class="[&_.t-form__item]:!mb-5">

        <t-form-item label="选择域名后缀">
          <t-select v-model="formData.domain_id" :disabled="formData.id !== 0" placeholder="请选择后缀" filterable class="!w-full">
            <t-option v-for="d in domainPool" :key="d.id" :value="d.id" :label="d.domain">
              {{ d.domain }}
            </t-option>
          </t-select>
        </t-form-item>

        <t-form-item v-if="currentDomainRemark" label="备注" class="!mb-4">
          <div class="w-full px-3 py-2 bg-zinc-100/80 dark:bg-zinc-800/40 rounded-lg text-[13px] text-[var(--td-text-color-secondary)] whitespace-pre-wrap break-all leading-relaxed border border-dashed border-zinc-200 dark:border-zinc-700">
            {{ currentDomainRemark }}
          </div>
        </t-form-item>

        <t-form-item label="子域名称">
          <template #help><span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">起一个你喜欢的前缀即可。</span></template>
          <t-input v-model="formData.name" placeholder="例如: myserver" class="!w-full">
            <template #prefix-icon>
              <span v-if="formMode === 'mc_srv' && formData.id === 0" class="text-[var(--td-text-color-secondary)] px-1.5 bg-zinc-100 dark:bg-zinc-800 mr-1 rounded font-mono text-xs flex items-center border border-zinc-200 dark:border-zinc-700">
                _minecraft._tcp.
              </span>
            </template>
          </t-input>
        </t-form-item>

        <template v-if="formMode === 'mc_srv' && formData.id === 0">
          <t-form-item label="选择隧道 (自动生成解析值)">
            <t-select v-model="selectedTunnelId" placeholder="点击选择已有的 TCP 隧道" @change="handleTunnelSelect" class="!w-full">
              <t-option
                v-for="t in tcpTunnels"
                :key="t.id"
                :value="t.id"
                :label="`${t.name} (端口: ${t.remote_port})`"
              />
            </t-select>
          </t-form-item>
        </template>

        <div class="flex flex-col sm:flex-row gap-0 sm:gap-4 w-full">
          <t-form-item label="记录类型" class="flex-1">
            <t-select v-model="formData.type" class="!w-full">
              <t-option label="A (IPv4)" value="A" />
              <t-option label="CNAME (别名)" value="CNAME" />
              <t-option label="AAAA (IPv6)" value="AAAA" />
              <t-option label="SRV (服务记录)" value="SRV" />
            </t-select>
          </t-form-item>

          <t-form-item label="解析记录值" class="flex-1">
            <t-input v-model="formData.record" placeholder="例: 5 5 25565 node.mslmc.net" class="!w-full !font-mono" />
          </t-form-item>
        </div>

        <div class="flex justify-end gap-3 mt-6 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-700">
          <t-button theme="default" variant="base" class="!bg-zinc-100 dark:!bg-zinc-800 !border-none !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 !rounded-lg !font-bold" @click="goBack">取消</t-button>
          <t-button theme="primary" :loading="submitting" class="!rounded-lg !font-bold shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50" @click="handleSubmit">
            {{ formData.id === 0 ? '立即创建' : '保存修改' }}
          </t-button>
        </div>

      </t-form>
    </div>
  </t-dialog>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

/* 滚动条混入 */
.custom-scrollbar {
  .scrollbar-mixin();
}

</style>
