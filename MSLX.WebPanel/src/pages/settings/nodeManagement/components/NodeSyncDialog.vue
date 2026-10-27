<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { useNodeStore, useTaskStore } from '@/store';
import { getPluginList } from '@/api/plugins';
import type { PluginListModel } from '@/api/model/plugins';
import { previewNodeSync, startNodeSync, type NodeSyncSelection } from '@/api/node';
import { request } from '@/utils/request';
import { invalidateNodePlugins } from '@/utils/pluginManager';

const props = defineProps<{ visible: boolean }>();
const emit = defineEmits(['update:visible']);
const nodeStore = useNodeStore();
const taskStore = useTaskStore();
const selectedNodes = ref<string[]>([]);
const selectedSettings = ref<string[]>([]);
const selectedPlugins = ref<string[]>([]);
const includeConfig = ref(false);
const plugins = ref<PluginListModel[]>([]);
const preview = ref<any>(null);
const busy = ref(false);
const task = ref<any>(null);
const taskId = ref('');
const taskError = ref('');
let pollTimer: ReturnType<typeof setInterval> | undefined;
let failures = 0;
let polling = false;
let revision = 0;
const fields = [
  ['allowNormalUserChangeUserName', '允许普通用户改名'], ['allowNormalUserEditFrpConfig', '允许普通用户编辑穿透配置'],
  ['allowNormalUserOfflineDownload', '允许普通用户离线下载'], ['enableSsrfProtection', '离线下载 SSRF 保护'],
  ['openWebConsoleOnLaunch', '启动时打开控制台'], ['neoForgeInstallerMirrors', '安装镜像源'], ['downloadThreadCount', '下载线程数'],
];
const fieldName = (key: string) => fields.find(field => field[0] === key)?.[1] || key;
const display = (value: any) => typeof value === 'boolean' ? (value ? '开启' : '关闭') : String(value ?? '未设置');
const nodeName = (id: string) => nodeStore.slaveNodes.find(node => node.nodeId === id)?.nodeName || id;
const targets = computed(() => nodeStore.slaveNodes.filter(node => !node.pendingRemoval).map(node => ({ label: node.nodeName, value: node.nodeId })));
const pluginOptions = computed(() => plugins.value.map(plugin => ({
  label: `${plugin.name} · v${plugin.version} · ${plugin.status}`,
  value: plugin.id, disabled: !plugin.syncInfo || plugin.syncInfo.pending,
})));
const running = computed(() => taskId.value && (!task.value || task.value.state === 0 || task.value.state === 1));
const selection = (): NodeSyncSelection => ({ targetNodeIds: [...selectedNodes.value], settingKeys: [...selectedSettings.value], pluginIds: [...selectedPlugins.value], includePluginConfig: includeConfig.value });
const stopPolling = () => { if (pollTimer) clearInterval(pollTimer); pollTimer = undefined; };

watch([selectedNodes, selectedSettings, selectedPlugins, includeConfig], () => { revision++; preview.value = null; }, { deep: true });
watch(() => props.visible, async visible => {
  stopPolling();
  if (!visible) return;
  selectedNodes.value = []; selectedSettings.value = []; selectedPlugins.value = [];
  includeConfig.value = false; preview.value = null; task.value = null; taskId.value = ''; taskError.value = ''; failures = 0;
  busy.value = true;
  try {
    plugins.value = await getPluginList({ includeSyncInfo: true }, 'local');
  } catch (error: any) { MessagePlugin.error('读取主节点插件失败: ' + error.message); }
  finally { busy.value = false; }
});
onUnmounted(stopPolling);

async function makePreview() {
  busy.value = true;
  const current = revision;
  try {
    const result = await previewNodeSync(selection());
    if (current === revision) preview.value = result;
  } catch (error: any) { preview.value = null; MessagePlugin.error(error.message); }
  finally { busy.value = false; }
}
async function poll() {
  if (polling || !props.visible || !taskId.value) return;
  polling = true;
  try {
    const tasks = await request.get<any[]>({ url: '/api/tasks' }, { targetNodeId: 'local', requestToSlaveNode: false });
    const found = tasks.find(item => item.id === taskId.value);
    if (!found) throw new Error('任务记录不存在或已过期');
    task.value = found; failures = 0; taskError.value = '';
    if (found.state >= 2) {
      stopPolling();
      selectedNodes.value.forEach(invalidateNodePlugins);
    }
  } catch (error: any) {
    failures++;
    taskError.value = `读取任务结果失败：${error.message}。任务可能仍在主节点运行，可稍后刷新结果。`;
    if (failures >= 3) stopPolling();
  } finally { polling = false; }
}
async function start() {
  if (!preview.value) return;
  busy.value = true;
  const reviewed = preview.value;
  const data = { ...selection(), sourceRevision: reviewed.sourceRevision,
    targetRevisions: Object.fromEntries(reviewed.targets.map((target: any) => [target.nodeId, target.revision])) };
  try {
    const result = await startNodeSync(data);
    taskId.value = result.taskId;
    if (taskStore.nodeId === 'local') void taskStore.fetchTasks();
    await poll();
    if (!task.value || task.value.state < 2) pollTimer = setInterval(poll, 1000);
    MessagePlugin.success('同步任务已创建，关闭弹窗后可在主节点后台任务中查看');
  } catch (error: any) { preview.value = null; MessagePlugin.error(error.message + '；请重新预览'); }
  finally { busy.value = false; }
}
async function cancel() {
  try {
    await request.post({ url: `/api/tasks/${taskId.value}/cancel` }, { targetNodeId: 'local', requestToSlaveNode: false });
    MessagePlugin.info('已请求取消，当前插件操作结束后停止后续项目');
    await poll();
  } catch (error: any) { MessagePlugin.error(error.message); }
}
</script>

<template>
  <t-dialog :visible="visible" @update:visible="emit('update:visible', $event)" header="从主节点同步" attach="body" top="3vh" width="min(900px, 94vw)" :close-on-overlay-click="false">
    <t-loading :loading="busy">
      <div class="flex flex-col gap-5 max-h-[min(65vh,calc(100dvh-180px))] overflow-y-auto pr-2">
        <t-alert theme="info" message="来源固定为主节点，只同步勾选内容，不删除目标节点额外安装的插件。监听地址、SSL、密钥和运行数据不会同步。" />
        <section class="flex flex-col gap-2">
          <h3 class="font-semibold">目标子节点</h3>
          <t-checkbox-group v-model="selectedNodes" :options="targets" :disabled="!!taskId || busy" />
          <span v-if="!targets.length" class="text-[var(--td-text-color-secondary)]">暂无可用子节点</span>
        </section>
        <section class="flex flex-col gap-2">
          <h3 class="font-semibold">同步设置（默认不选）</h3>
          <t-checkbox-group v-model="selectedSettings" :options="fields.map(field => ({ value: field[0], label: field[1] }))" :disabled="!!taskId || busy" />
        </section>
        <section class="flex flex-col gap-2">
          <h3 class="font-semibold">同步插件程序及启停状态</h3>
          <t-checkbox-group v-model="selectedPlugins" :options="pluginOptions" :disabled="!!taskId || busy" />
          <span v-if="!plugins.length" class="text-[var(--td-text-color-secondary)]">主节点暂无插件</span>
          <t-checkbox v-model="includeConfig" :disabled="!!taskId || busy || !selectedPlugins.length">同时覆盖插件标准配置 Config.json</t-checkbox>
          <t-alert v-if="includeConfig" theme="warning" message="标准配置可能包含访问密钥和本机路径，确认后会原样覆盖。源配置不存在时跳过，数据库和其他运行文件保持独立。" />
        </section>
        <section v-if="preview" class="flex flex-col gap-3">
          <h3 class="font-semibold">差异预览</h3>
          <div v-for="target in preview.targets" :key="target.nodeId" class="rounded-xl border border-[var(--td-component-border)] p-4 flex flex-col gap-2">
            <strong>{{ target.nodeName }}</strong>
            <div v-for="setting in target.settings" :key="setting.key" class="text-sm break-words">
              {{ fieldName(setting.key) }}：{{ display(setting.target) }} → {{ display(setting.source) }}{{ setting.changed ? '' : '（已一致）' }}
            </div>
            <div v-for="plugin in target.plugins" :key="plugin.id" class="text-sm break-words">
              {{ plugin.id }}：{{ plugin.targetVersion || '未安装' }} → {{ plugin.sourceVersion }}；状态 {{ plugin.targetVersion ? plugin.targetEnabled ? '启用' : '禁用' : '未安装' }} → {{ plugin.sourceEnabled ? '启用' : '禁用' }}
              <span v-if="plugin.programChanged">（将按主节点版本覆盖，可能降级）</span>
              <span v-if="includeConfig">；配置{{ !plugin.configAvailable ? '源不存在，将跳过' : plugin.configChanged ? '将覆盖' : '已一致' }}</span>
            </div>
          </div>
        </section>
        <section v-if="taskId" class="flex flex-col gap-3">
          <t-alert v-if="taskError" theme="warning" :message="taskError" />
          <t-progress :percentage="task?.progress || 0" :status="task?.state === 3 ? 'error' : task?.state === 2 ? 'success' : 'active'" />
          <p>{{ task?.message || '等待任务状态…' }}</p>
          <div v-for="(item, index) in task?.syncResults || []" :key="index" class="text-sm break-words">
            <t-tag :theme="item.status === 'failed' ? 'danger' : item.status === 'success' ? 'success' : 'default'" variant="light">{{ ({ pending: '等待', running: '执行中', success: '成功', failed: '失败', skipped: '跳过' } as any)[item.status] }}</t-tag>
            {{ nodeName(item.nodeId) }} · {{ item.item === 'settings' ? '所选设置' : item.item }}：{{ item.message }}
          </div>
        </section>
      </div>
    </t-loading>
    <template #footer>
      <div class="flex flex-wrap justify-end gap-2">
        <t-button v-if="taskId" variant="outline" @click="poll">刷新任务结果</t-button>
        <t-button v-if="running" theme="danger" variant="outline" @click="cancel">停止后续同步</t-button>
        <t-button v-if="!taskId" :loading="busy" :disabled="!selectedNodes.length || !selectedSettings.length && !selectedPlugins.length" variant="outline" @click="makePreview">预览差异</t-button>
        <t-button v-if="preview && !taskId" theme="primary" :loading="busy" @click="start">确认同步</t-button>
        <t-button variant="text" @click="emit('update:visible', false)">关闭</t-button>
      </div>
    </template>
  </t-dialog>
</template>
