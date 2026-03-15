<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { request } from '@/utils/request';
import { generateRandomString } from '@/utils/tools';

interface NodeInfo {
  id: number;
  name: string;
  area: string;
  nodegroup: string;
  notes: string;
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
  nodeName: '', // ChmlFrp使用节点名称绑定
  porttype: 'TCP',
  tunnelname: '',
  localip: '127.0.0.1',
  localport: '25565',
  remoteport: '',
});

const selectedNode = computed(() => nodes.value.find((n) => n.name === form.nodeName) || null);

// 按照 nodegroup 对节点进行分组
const groupedNodes = computed(() => {
  const groupsMap = new Map<string, { label: string; value: string; children: NodeInfo[] }>();

  nodes.value.forEach((node) => {
    let groupLabel = node.nodegroup;
    if (groupLabel === 'vip') groupLabel = 'VIP 节点';
    else if (groupLabel === 'user') groupLabel = '普通节点';

    if (!groupsMap.has(node.nodegroup)) {
      groupsMap.set(node.nodegroup, { label: groupLabel, value: node.nodegroup, children: [] });
    }
    groupsMap.get(node.nodegroup)!.children.push(node);
  });

  return Array.from(groupsMap.values());
});

const generateRandomData = () => {
  form.tunnelname = 'MSL_' + generateRandomString(6);
  form.remoteport = (Math.floor(Math.random() * (65535 - 10000 + 1)) + 10000).toString();
};

const fetchNodes = async () => {
  loading.value = true;
  try {
    const res = await request.get(
      {
        url: '/node',
        baseURL: 'https://cf-v2.uapis.cn',
        headers: { Authorization: `Bearer ${props.token}` },
      },
      { withToken: false },
    );

    const resData = res?.code === 200 ? res.data : res;

    if (Array.isArray(resData)) {
      nodes.value = resData;
      if (props.visible && nodes.value.length > 0 && !form.nodeName) {
        form.nodeName = nodes.value[0].name;
        generateRandomData();
      }
    }
  } catch (e: any) {
    const errorMsg = e.response?.data?.msg || e.msg || e.message || '未知错误';
    MessagePlugin.error('加载节点失败: ' + errorMsg);
  } finally {
    loading.value = false;
  }
};

watch(
  () => props.visible,
  (val) => {
    if (val) {
      if (nodes.value.length > 0) {
        if (!form.nodeName) form.nodeName = nodes.value[0].name;
        generateRandomData();
      } else {
        fetchNodes();
      }
    }
  },
);

const handleConfirm = async () => {
  if (!form.nodeName) {
    MessagePlugin.warning('请选择一个节点');
    return;
  }
  if (!form.tunnelname || !form.localip || !form.localport) {
    MessagePlugin.warning('请填写完整的映射配置');
    return;
  }

  submitting.value = true;
  try {
    const res: any = await request.post(
      {
        url: '/create_tunnel',
        baseURL: 'https://cf-v2.uapis.cn',
        headers: { Authorization: `Bearer ${props.token}` },
        data: {
          token: props.token,
          tunnelname: form.tunnelname,
          node: form.nodeName,
          localip: form.localip,
          porttype: form.porttype,
          localport: parseInt(form.localport),
          encryption: false,
          compression: false,
          extraparams: '',
          remoteport: parseInt(form.remoteport) || 0,
        },
      },
      { withToken: false },
    );

    if (res && res.code && res.code !== 200) {
      throw new Error(res.msg || '指定的端口不合法或发生未知错误');
    }

    MessagePlugin.success(`隧道 ${form.tunnelname} 创建成功！`);
    emit('success');
    emit('update:visible', false);
  } catch (e: any) {
    const errorMsg = e.message || e.response?.data?.msg || e.msg || '请检查配置或节点状态';
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
    header="新建 ChmlFrp 隧道"
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
        <t-form-item label="选择节点" name="nodeName">
          <t-select v-model="form.nodeName" placeholder="请选择节点" @change="generateRandomData" :popup-props="{ overlayClassName: 'max-h-[300px]' }">
            <t-option-group v-for="group in groupedNodes" :key="group.value" :label="group.label">
              <t-option v-for="node in group.children" :key="node.id" :value="node.name" :label="node.name">
                <div class="flex justify-between items-center w-full">
                  <span class="truncate">{{ node.name }}</span>
                  <span class="text-xs text-zinc-400 shrink-0 ml-2">{{ node.area }}</span>
                </div>
              </t-option>
            </t-option-group>
          </t-select>
        </t-form-item>

        <t-form-item v-if="selectedNode" label="节点详情">
          <div class="w-full flex flex-col gap-2.5">
            <div class="bg-[var(--td-bg-color-secondarycontainer)] rounded-[var(--td-radius-medium)] p-3 border border-dashed border-[var(--td-component-border)]">
              <pre class="m-0 whitespace-pre-wrap break-all text-[13px] text-[var(--td-text-color-primary)] leading-[1.6]">{{ selectedNode.notes || '此节点暂无备注' }}</pre>
            </div>
          </div>
        </t-form-item>

        <t-form-item label="隧道类型">
          <t-select v-model="form.porttype">
            <t-option label="TCP" value="TCP" />
            <t-option label="UDP" value="UDP" />
            <t-option label="HTTP" value="HTTP" />
            <t-option label="HTTPS" value="HTTPS" />
          </t-select>
        </t-form-item>

        <t-row :gutter="[16, 20]">
          <t-col :xs="12" :sm="6">
            <t-form-item label="隧道名称">
              <t-input v-model="form.tunnelname" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="远程端口">
              <t-input v-model="form.remoteport" placeholder="留空由服务端分配">
                <template #suffix>
                  <t-button variant="text" size="small" @click="generateRandomData">随机</t-button>
                </template>
              </t-input>
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地IP">
              <t-input v-model="form.localip" />
            </t-form-item>
          </t-col>
          <t-col :xs="12" :sm="6">
            <t-form-item label="本地端口">
              <t-input v-model="form.localport" />
            </t-form-item>
          </t-col>
        </t-row>

      </t-form>
    </t-loading>
  </t-dialog>
</template>

<style scoped>
</style>
