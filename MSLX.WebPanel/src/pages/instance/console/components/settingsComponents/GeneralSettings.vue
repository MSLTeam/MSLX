<script setup lang="ts">
import { ref, onMounted, watch, nextTick, onUnmounted, computed } from 'vue';
import { useRoute } from 'vue-router';
import {
  MessagePlugin,
  type FormRules,
  type FormInstanceFunctions,
  DialogPlugin,
  NotifyPlugin,
} from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';
import { LockOnIcon, LockOffIcon } from 'tdesign-icons-vue-next';

// API
import { getInstanceSettings, postInstanceSettings } from '@/api/instance';
import { getJavaVersionList } from '@/api/mslapi/java';
import { getLocalJavaList } from '@/api/localJava';
import { initUpload, uploadChunk, finishUpload, deleteUpload } from '@/api/files';
import { type UpdateInstanceModel } from '@/api/model/instance';

// 组件
import ServerCoreSelector from '@/pages/instance/createInstance/components/ServerCoreSelector.vue';

const route = useRoute();
const userStore = useUserStore();

const instanceId = computed(() => {
  const idStr = route.params.serverId as string;
  if (!idStr) return NaN;
  return parseInt(idStr);
});

const formRef = ref<FormInstanceFunctions | null>(null);
const loading = ref(false);
const submitting = ref(false);

// 路径编辑状态
const isPathEditable = ref(false);

// 编码选项
const encodingOptions = [
  { label: 'UTF-8', value: 'utf-8' },
  { label: 'GBK', value: 'gbk' },
];

const fileEncodingOptions = [
  { label: 'UTF-8', value: 'utf-8' },
  { label: 'UTF-8 With BOM', value: 'utf-8-bom' },
  { label: 'GBK', value: 'gbk' },
];

// 数据模型
const formData = ref<UpdateInstanceModel>({
  id: instanceId.value,
  name: '',
  base: '',
  java: '',
  core: '',
  minM: 1024,
  maxM: 4096,
  args: '',
  yggdrasilApiAddr: '',
  backupMaxCount: 20,
  backupDelay: 10,
  backupPath: 'MSLX://Backup/Instance',
  monitorPlayers: true,
  autoRestart: false,
  forceAutoRestart: true,
  ignoreEula: false,
  runOnStartup: false,
  inputEncoding: 'utf-8',
  outputEncoding: 'utf-8',
  fileEncoding: 'utf-8',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
});

// --- 内存单位转换 ---

const minUnit = ref('MB');
const maxUnit = ref('MB');
const unitOptions = [
  { label: 'MB', value: 'MB' },
  { label: 'GB', value: 'GB' },
];

// 计算属性
const minMComputed = computed({
  get: () => {
    if (minUnit.value === 'GB') {
      const val = formData.value.minM / 1024;
      // 读取时保留 2 位小数
      return Math.round(val * 100) / 100;
    }
    return formData.value.minM;
  },
  set: (val) => {
    formData.value.minM = minUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});

const maxMComputed = computed({
  get: () => {
    if (maxUnit.value === 'GB') {
      const val = formData.value.maxM / 1024;
      return Math.round(val * 100) / 100;
    }
    return formData.value.maxM;
  },
  set: (val) => {
    formData.value.maxM = maxUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});

// --- Java 逻辑 ---
const javaType = ref('custom');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersions = ref<{ label: string; value: string }[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');

const fetchJavaVersions = async (force = false) => {
  try {
    if (force) MessagePlugin.info('正在扫描 Java 环境...');
    const res = await getJavaVersionList(
      userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', ''),
      userStore.userInfo.systemInfo.osArchitecture.toLowerCase(),
    );
    if (res && Array.isArray(res)) {
      javaVersions.value = res.map((v) => ({ label: `Java ${v} (在线)`, value: v }));
    }
    const locals = await getLocalJavaList(force);
    localJavaVersions.value = locals.map((v) => ({
      label: `Java ${v.version} (${v.path})`,
      value: v.path,
    }));
    if (force) MessagePlugin.success('刷新成功');
  } catch (e: any) {
    console.error(e);
  }
};

// 监听 Java 类型变化
watch([javaType, selectedJavaVersion, customJavaPath], ([type, ver, path]) => {
  if (type === 'none') formData.value.java = 'none';
  else if (type === 'env') formData.value.java = 'java';
  else if (type === 'custom' || type === 'local') formData.value.java = path;
  else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';
});

// --- 备份路径逻辑 ---
const backupLocationType = ref('MSLX://Backup/Instance');
const customBackupPath = ref('');

watch([backupLocationType, customBackupPath], ([type, customVal]) => {
  if (type === 'custom') {
    formData.value.backupPath = customVal;
  } else {
    formData.value.backupPath = type;
  }
});

// --- 外置登录逻辑 ---
const authSelectType = ref('');
const customAuthUrl = ref('');
const authOptions = [
  { label: '官方/离线模式 (无)', value: 'none' },
  { label: 'MSL 统一身份验证 (MSL Skin)', value: 'https://skin.mslmc.net/api/yggdrasil' },
  { label: 'LittleSkin', value: 'https://littleskin.cn/api/yggdrasil' },
  { label: '自定义服务器', value: 'custom' },
];

watch([authSelectType, customAuthUrl], ([type, url]) => {
  if (type === 'none') {
    formData.value.yggdrasilApiAddr = '';
  } else if (type === 'custom') {
    formData.value.yggdrasilApiAddr = url;
  } else {
    formData.value.yggdrasilApiAddr = type;
  }
});

// --- 核心更新逻辑 ---
const showCoreTools = ref(false);
const showCoreSelector = ref(false);
const uploadInputRef = ref<HTMLInputElement | null>(null);
const isUploading = ref(false);
const uploadProgress = ref(0);
const uploadedFileName = ref('');

const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = '';
  showCoreTools.value = false;
  MessagePlugin.success(`已选择核心: ${data.filename}，保存后将自动下载`);
};

const triggerFileSelect = () => uploadInputRef.value?.click();
const onFileChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (!input.files?.length) return;
  const file = input.files[0];

  if (formData.value.coreFileKey) {
    try {
      await deleteUpload(formData.value.coreFileKey);
    } catch {
      console.error('删除上传失败');
    }
  }

  uploadedFileName.value = file.name;
  isUploading.value = true;
  uploadProgress.value = 0;

  try {
    const initRes = await initUpload();
    const uploadId = (initRes as any).uploadId;
    const chunkSize = 5 * 1024 * 1024;
    const totalChunks = Math.ceil(file.size / chunkSize);

    for (let i = 0; i < totalChunks; i++) {
      const chunk = file.slice(i * chunkSize, Math.min(file.size, (i + 1) * chunkSize));
      await uploadChunk(uploadId, i, chunk);
      uploadProgress.value = Math.floor(((i + 1) / totalChunks) * 100);
    }

    const finishRes = await finishUpload(uploadId, totalChunks);
    formData.value.coreFileKey = (finishRes as any).uploadId;
    formData.value.core = file.name;
    formData.value.coreUrl = '';

    MessagePlugin.success('文件上传就绪，保存后生效');
    showCoreTools.value = false;
  } catch (e: any) {
    MessagePlugin.error(`上传失败: ${e.message}`);
  } finally {
    isUploading.value = false;
    input.value = '';
  }
};

// 路径修改
const togglePathEdit = () => {
  if (!isPathEditable.value) {
    NotifyPlugin.warning({
      title: '风险操作',
      content: '修改实例路径会导致面板无法找到原有文件。请确保您已手动移动了文件，或您明确知道自己在做什么。',
      duration: 5000,
    });
  }
  isPathEditable.value = !isPathEditable.value;
};

// --- 提交与 SignalR ---
const showProgressDialog = ref(false);
const progressPercent = ref(0);
const progressLogs = ref<{ time: string; msg: string }[]>([]);
const hubConnection = ref<HubConnection | null>(null);

// 动态计算校验规则
const rules = computed<FormRules>(() => {
  // 如果是自定义模式，跳过核心和内存的校验
  if (javaType.value === 'none') {
    return {
      name: [{ required: true, message: '服务器名称不能为空', trigger: 'blur' }],
      base: [{ required: true, message: '基础路径不能为空', trigger: 'blur' }],
      args: [{ required: true, message: '自定义模式必须填写启动命令', trigger: 'blur' }],
    };
  }

  return {
    name: [{ required: true, message: '服务器名称不能为空', trigger: 'blur' }],
    base: [{ required: true, message: '基础路径不能为空', trigger: 'blur' }],
    java: [{ required: true, message: 'Java 环境不能为空', trigger: 'change' }],
    core: [{ required: true, message: '核心文件名不能为空', trigger: 'change' }],
    minM: [{ required: true, message: '必填', trigger: 'blur' }],
    maxM: [{ required: true, message: '必填', trigger: 'blur' }],
  };
});

const initData = async () => {
  if (!instanceId.value) {
    return;
  }
  loading.value = true;
  try {
    await fetchJavaVersions();
    formData.value.id = instanceId.value;
    const res = await getInstanceSettings(instanceId.value);
    formData.value = {
      ...formData.value,
      ...res,
      coreUrl: '',
      coreFileKey: '',
      coreSha256: '',
    };

    // 判断内存单位
    minUnit.value = res.minM > 0 && res.minM % 1024 === 0 ? 'GB' : 'MB';
    maxUnit.value = res.maxM > 0 && res.maxM % 1024 === 0 ? 'GB' : 'MB';

    isPathEditable.value = false;

    // 解析 Java 类型
    if (res.java === 'none') {
      javaType.value = 'none';
    } else if (res.java === 'java') {
      javaType.value = 'env';
    } else if (res.java.startsWith('MSLX://Java/')) {
      javaType.value = 'online';
      selectedJavaVersion.value = res.java.replace('MSLX://Java/', '');
    } else {
      const inLocal = localJavaVersions.value.find((v) => v.value === res.java);
      javaType.value = inLocal ? 'local' : 'custom';
      customJavaPath.value = res.java;
    }

    // 解析备份路径
    const bPath = res.backupPath;
    if (bPath === 'MSLX://Backup/Instance' || bPath === 'MSLX://Backup/Data') {
      backupLocationType.value = bPath;
    } else {
      backupLocationType.value = 'custom';
      customBackupPath.value = bPath;
    }

    // 解析外置登录
    const authUrl = res.yggdrasilApiAddr;
    if (!authUrl) {
      authSelectType.value = 'none';
    } else if (authUrl === 'https://skin.mslmc.net/api/yggdrasil') {
      authSelectType.value = 'https://skin.mslmc.net/api/yggdrasil';
    } else if (authUrl === 'https://littleskin.cn/api/yggdrasil') {
      authSelectType.value = 'https://littleskin.cn/api/yggdrasil';
    } else {
      authSelectType.value = 'custom';
      customAuthUrl.value = authUrl;
    }
  } catch (e: any) {
    MessagePlugin.error('获取配置失败: ' + e.message);
  } finally {
    loading.value = false;
  }
};

watch(
  () => route.params.serverId,
  (newId) => {
    if (route.name !== 'InstanceConsole') {
      return;
    }

    if (newId) {
      initData();
    }
  },
);
onMounted(initData);

const onSubmit = async () => {
  const result = await formRef.value?.validate();
  if (result !== true) return;

  // 自定义模式下，不需要检查核心变更
  if (javaType.value !== 'none') {
    const isChangingCore = !!formData.value.coreUrl || !!formData.value.coreFileKey;
    if (isChangingCore) {
      const confirm = await new Promise((resolve) => {
        const dialog = DialogPlugin.confirm({
          header: '确认变更核心文件?',
          body: '检测到您上传或选择了新的核心文件，这将覆盖服务器现有的部署。',
          theme: 'warning',
          onConfirm: () => {
            dialog.hide();
            resolve(true);
          },
          onClose: () => {
            dialog.hide();
            resolve(false);
          },
        });
      });
      if (!confirm) return;
    }
  }

  // 处理自定义模式的写死参数
  if (javaType.value === 'none') {
    formData.value.core = 'none';
    formData.value.minM = 1027; // 按要求写死
    formData.value.maxM = 1102; // 按要求写死
    formData.value.java = 'none';
    // 清空下载参数以防冲突
    formData.value.coreUrl = '';
    formData.value.coreFileKey = '';
    formData.value.coreSha256 = '';
  }

  submitting.value = true;
  try {
    const res = await postInstanceSettings(formData.value);
    const needListen = (res as any).data?.needListen ?? (res as any).needListen;

    if (needListen) {
      startSignalRListening();
    } else {
      MessagePlugin.success('配置已保存');
      submitting.value = false;
      initData();
    }
  } catch (e: any) {
    MessagePlugin.error(e.message || '保存失败');
    submitting.value = false;
  }
};

const startSignalRListening = async () => {
  showProgressDialog.value = true;
  progressPercent.value = 0;
  progressLogs.value = [];

  const { baseUrl, token } = userStore;
  const hubUrl = new URL('/api/hubs/updateProgressHub', baseUrl || window.location.origin);
  hubUrl.searchParams.append('x-user-token', token);

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.None)
    .build();

  hubConnection.value.on('UpdateStatus', (msg: string, prog: number, isErr: boolean) => {
    progressLogs.value.push({ time: new Date().toLocaleTimeString(), msg: isErr ? `[错误] ${msg}` : msg });
    nextTick(() => {
      const div = document.getElementById('update-log-box');
      if (div) div.scrollTop = div.scrollHeight;
    });
    if (prog >= 0) progressPercent.value = Number(prog.toFixed(1));
    if (prog === 100) {
      MessagePlugin.success('更新完成');
      closeSignalR(true);
    } else if (isErr || prog === -1) {
      // 错误不自动关闭
    }
  });

  try {
    await hubConnection.value.start();
    await hubConnection.value.invoke('JoinGroup', instanceId.value.toString());
  } catch {
    progressLogs.value.push({ time: '-', msg: '连接失败' });
  }
};

const closeSignalR = (success = false) => {
  hubConnection.value?.stop();
  setTimeout(() => {
    showProgressDialog.value = false;
    submitting.value = false;
    if (success) initData();
  }, 1000);
};

onUnmounted(() => {
  hubConnection.value?.stop();
});
</script>

<template>
  <div class="flex flex-col mx-auto w-full relative">
    <t-loading :loading="loading" show-overlay>
      <t-form
        ref="formRef"
        :disabled="!userStore.isAdmin"
        :data="formData"
        :rules="rules"
        label-width="0"
        @submit="onSubmit"
      >
        <div
          class="flex items-center gap-2 mt-5 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
        >
          <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
          <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">基础设置</h2>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">服务器名称</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              在面板列表中显示的别名，支持中文
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center">
            <t-input v-model="formData.name" placeholder="请输入名称" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">实例路径</div>
            <div
              class="text-xs mt-1 leading-relaxed"
              :class="isPathEditable ? 'text-amber-500' : 'text-[var(--td-text-color-secondary)]'"
            >
              {{ isPathEditable ? '警告：修改路径可能导致无法找到原文件' : '服务器文件的物理存储路径，非必要请勿修改' }}
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center">
            <t-input v-model="formData.base" :disabled="!isPathEditable" class="w-full">
              <template #suffix>
                <t-tooltip :content="isPathEditable ? '点击锁定' : '点击解锁编辑 (慎重)'">
                  <t-button
                    variant="text"
                    shape="square"
                    class="!rounded-md hover:!bg-zinc-100 dark:hover:!bg-zinc-800"
                    @click="togglePathEdit"
                  >
                    <template #icon>
                      <lock-off-icon v-if="isPathEditable" class="text-amber-500" />
                      <lock-on-icon v-else class="text-zinc-400" />
                    </template>
                  </t-button>
                </t-tooltip>
              </template>
            </t-input>
          </div>
        </div>

        <div
          class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
        >
          <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
          <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">运行模式</h2>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">启动方式</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              选择使用 Java 启动 Minecraft，或使用自定义命令启动其他程序 (如 Bedrock, Python 等)
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
            <t-select
              v-model="javaType"
              class="w-full"
              :options="[
                { label: 'MSLX 在线下载 (Java)', value: 'online' },
                { label: '使用本地版本 (Java)', value: 'local' },
                { label: '自定义路径 (Java)', value: 'custom' },
                { label: '环境变量 (Java)', value: 'env' },
                { label: '自定义命令 (无Java)', value: 'none' },
              ]"
            />
            <div v-if="javaType !== 'none'" class="w-full">
              <t-select
                v-if="javaType === 'online'"
                v-model="selectedJavaVersion"
                :options="javaVersions"
                placeholder="请选择版本"
                filterable
              />
              <t-select
                v-if="javaType === 'local'"
                v-model="customJavaPath"
                :options="localJavaVersions"
                placeholder="选择已识别的 Java"
              />
              <t-input
                v-if="javaType === 'custom'"
                v-model="customJavaPath"
                placeholder="输入 java 可执行文件完整路径"
              />
            </div>
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
              {{ javaType === 'none' ? '启动命令 (Command)' : '启动参数 (JVM Args)' }}
            </div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              {{
                javaType === 'none'
                  ? '完全自定义的启动命令。程序将直接执行此段内容，不依赖 Java 环境。'
                  : '传递给 Java 的启动参数，如 GC 策略 (例如 -XX:+UseG1GC)'
              }}
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center">
            <t-textarea
              v-model="formData.args"
              :autosize="{ minRows: 2, maxRows: 4 }"
              class="w-full"
              :placeholder="javaType === 'none' ? '例如: ./bedrock_server_x64' : '无特殊需求请留空'"
            />
          </div>
        </div>

        <template v-if="javaType !== 'none'">
          <div
            class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
          >
            <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
            <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">核心管理</h2>
          </div>

          <div
            class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
          >
            <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
              <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">服务端核心文件</div>
              <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                指定启动的 Jar 文件名。如果文件已存在于目录中，直接输入文件名即可。
                <br />需要更新核心？点击下方“文件工具”
              </div>
            </div>
            <div class="w-full md:w-[340px] shrink-0 flex flex-col">
              <t-input v-model="formData.core" placeholder="例如 server.jar" class="w-full">
                <template #suffix>
                  <t-button
                    variant="text"
                    theme="primary"
                    size="small"
                    class="!rounded-md"
                    @click="showCoreTools = !showCoreTools"
                  >
                    {{ showCoreTools ? '收起工具' : '文件工具' }}
                  </t-button>
                </template>
              </t-input>

              <div
                v-if="showCoreTools"
                class="mt-3 p-3 bg-zinc-50 dark:bg-zinc-800/50 border border-zinc-200 dark:border-zinc-700 rounded-lg flex flex-col gap-3"
              >
                <t-alert
                  theme="info"
                  message="在此处操作会自动下载/上传文件，并填入上方的文件名。"
                  class="!py-1.5 !px-3 !rounded-md text-xs"
                />
                <div class="flex gap-2">
                  <t-button
                    block
                    variant="outline"
                    class="!rounded-md bg-white dark:bg-zinc-900"
                    @click="showCoreSelector = true"
                  >
                    <template #icon><t-icon name="cloud-download" /></template>版本库
                  </t-button>
                  <t-button
                    block
                    variant="outline"
                    class="!rounded-md bg-white dark:bg-zinc-900"
                    :loading="isUploading"
                    @click="triggerFileSelect"
                  >
                    <template #icon><t-icon name="upload" /></template>本地上传
                  </t-button>
                </div>
                <input ref="uploadInputRef" type="file" accept=".jar" hidden @change="onFileChange" />
                <div v-if="isUploading" class="flex flex-col gap-1 mt-1 text-xs text-zinc-500">
                  <span>正在上传: {{ uploadedFileName }}</span>
                  <t-progress theme="line" :percentage="uploadProgress" />
                </div>
              </div>
            </div>
          </div>
        </template>

        <template v-if="javaType !== 'none'">
          <div
            class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
          >
            <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
            <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">资源限制</h2>
          </div>

          <div
            class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
          >
            <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
              <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">内存分配</div>
              <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                设置 Java 堆内存大小 (Xms / Xmx)
              </div>
            </div>

            <div class="w-full md:w-[340px] shrink-0 flex items-center gap-2">
              <div class="memory-input-group">
                <t-input-number
                  v-model="minMComputed"
                  :min="0"
                  :decimal-places="minUnit === 'GB' ? 1 : 0"
                  placeholder="Xms"
                  theme="normal"
                  class="input-left"
                />
                <t-select v-model="minUnit" :options="unitOptions" :clearable="false" class="select-right" />
              </div>
              <span class="text-zinc-400 mx-1 shrink-0">-</span>
              <div class="memory-input-group">
                <t-input-number
                  v-model="maxMComputed"
                  :min="0"
                  :decimal-places="maxUnit === 'GB' ? 1 : 0"
                  placeholder="Xmx"
                  theme="normal"
                  class="input-left"
                />
                <t-select v-model="maxUnit" :options="unitOptions" :clearable="false" class="select-right" />
              </div>
            </div>
          </div>
        </template>

        <div
          class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
        >
          <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
          <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">备份设置</h2>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">备份策略</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              设置自动备份保留的最大数量，以及触发备份的延迟时间
            </div>

            <div class="mt-2">
              <t-tooltip
                content="MSLX 向服务器发送 save-all 指令后，会等待指定的秒数，确保数据完全写入硬盘后再开始打包备份。"
              >
                <span class="text-xs text-zinc-400 hover:text-zinc-500 cursor-help flex items-center gap-1 w-max">
                  <t-icon name="help-circle" /> 什么是延迟时间？
                </span>
              </t-tooltip>
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center gap-3 overflow-hidden">
            <t-input-number
              v-model="formData.backupMaxCount"
              :min="1"
              :max="100"
              placeholder="保留份数"
              theme="column"
              class="flex-1 min-w-0"
              suffix="份"
            />
            <span class="text-zinc-400 shrink-0">/</span>
            <t-input-number
              v-model="formData.backupDelay"
              :min="0"
              placeholder="延迟时间"
              theme="column"
              class="flex-1 min-w-0"
              suffix="秒"
            />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">备份存放路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              选择备份文件存储的位置。推荐存储在实例文件夹外部以免误删。
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
            <t-select
              class="w-full"
              v-model="backupLocationType"
              :options="[
                { label: '实例文件夹内 (Instance)', value: 'MSLX://Backup/Instance' },
                { label: '全局数据目录 (Data)', value: 'MSLX://Backup/Data' },
                { label: '自定义绝对路径', value: 'custom' },
              ]"
            />
            <t-input
              v-if="backupLocationType === 'custom'"
              v-model="customBackupPath"
              placeholder="输入备份存放的绝对路径"
              class="w-full"
            />
          </div>
        </div>

        <template v-if="javaType !== 'none'">
          <div
            class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
          >
            <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
            <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">外置登录</h2>
          </div>

          <div
            class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
          >
            <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
              <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">Yggdrasil API</div>
              <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                选择认证服务器。留空则表示使用官方正版登录 (或离线模式)。
              </div>
            </div>
            <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
              <t-select v-model="authSelectType" :options="authOptions" class="w-full" />
              <t-input
                v-if="authSelectType === 'custom'"
                v-model="customAuthUrl"
                placeholder="输入 Authlib-Injector API 地址"
                class="w-full"
              />
            </div>
          </div>
        </template>

        <div
          class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
        >
          <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
          <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">高级设置</h2>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">停止服务器指令</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              设置正常停止时所发送的指令，默认为stop<br />设置为^c为发送Ctrl C (在部分环境可能无法发送成功)
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center">
            <t-input v-model="formData.stopCommand" placeholder="请输入停止指令" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">日志原彩显示</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              开启此选项后，将注入相关环境变量，以让MC终端日志输出原有的色彩样式<br />此功能可以和日志染色功能搭配使用
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.allowOriginASCIIColors" size="large" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">玩家监控</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              开启此选项后，MSLX将自动为您监控在线的玩家列表<br />以及使用可视化黑白名单/管理员等功能
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.monitorPlayers" size="large" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">自动重启</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              当服务器崩溃或意外停止时尝试自动重启<br />熔断机制: 若5分钟内尝试重启次数达到 5 次，则停止尝试重启
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.autoRestart" size="large" />
          </div>
        </div>

        <div
          v-if="formData.autoRestart"
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">强制自动重启</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              开启此功能后，就算服务器是正常退出的也会强制重启(正常退出 => 退出代码 0)<br />不影响手动在面板关闭服务器
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.forceAutoRestart" size="large" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">关服强制结束时间</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              设置在发出Stop指令或关服请求后，等待多久后强制结束进程<br />可设置10 - 120 s
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-input-number v-model="formData.forceExitDelay" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">忽略EULA提示</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              若您的实例并非MC服务器，可打开此选项
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.ignoreEula" size="large" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">随守护进程启动</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              当物理机开机/面板启动时，自动启动此实例
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.runOnStartup" size="large" />
          </div>
        </div>

        <div
          v-if="javaType !== 'none'"
          class="flex flex-col md:flex-row md:items-center justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">强制Java使用UTF8</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              此功能可以解决部分游戏内中文乱码的问题（特别是Windows系统上）<br />开启此功能后请务必将下面的<b>文件编码</b>设置设置为<b
                >UTF-8</b
              >
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex md:justify-end items-center">
            <t-switch v-model="formData.forceJvmUTF8" size="large" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">控制台编码</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              设置输入输出流的字符集，乱码时请尝试切换
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center gap-3 overflow-hidden">
            <t-select v-model="formData.inputEncoding" :options="encodingOptions" label="输入" class="flex-1 min-w-0" />
            <t-select
              v-model="formData.outputEncoding"
              :options="encodingOptions"
              label="输出"
              class="flex-1 min-w-0"
            />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">文件编码</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              设置文件编辑和保存时的编码格式，乱码时请尝试切换。(一般Windows是GBK，其他是UTF-8。)
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-select v-model="formData.fileEncoding" :options="fileEncodingOptions" class="w-full" />
          </div>
        </div>

        <div
          v-if="userStore.isAdmin"
          class="sticky bottom-1 z-50 ml-auto w-max flex items-center gap-2 p-1.5 mt-2 mb-2 border border-zinc-200/80 dark:border-zinc-800 bg-white dark:bg-zinc-900 shadow-xl shadow-black/10 dark:shadow-black/40 rounded-full transition-all"
        >
          <t-button
            theme="default"
            variant="text"
            class="!rounded-full !px-5 text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
            @click="initData"
          >
            重置更改
          </t-button>

          <t-button
            theme="primary"
            type="submit"
            class="!rounded-full !px-6 shadow-md shadow-[var(--color-primary)]/30"
            :loading="submitting"
          >
            保存设置
          </t-button>
        </div>
      </t-form>
    </t-loading>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />

    <t-dialog
      v-model:visible="showProgressDialog"
      header="正在应用更新"
      :footer="false"
      :close-on-overlay-click="false"
      :close-btn="false"
      width="600px"
      attach="body"
    >
      <div class="flex flex-col gap-4 pt-2">
        <t-progress theme="plump" :percentage="progressPercent" :label="`${progressPercent}%`" />
        <div
          class="h-48 bg-zinc-950 rounded-xl p-3 overflow-y-auto font-mono text-xs text-zinc-300 shadow-inner border border-zinc-800"
        >
          <div v-for="(log, idx) in progressLogs" :key="idx" class="mb-1 leading-relaxed">
            <span class="text-zinc-600 mr-2 select-none">{{ log.time }}</span> {{ log.msg }}
          </div>
        </div>
        <div v-if="progressPercent === 100" class="text-right mt-2">
          <t-button theme="primary" class="!rounded-lg" @click="showProgressDialog = false">关闭并刷新</t-button>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* === 内存输入框 === */
.memory-input-group {
  display: flex;
  align-items: center;
  max-width: 110px;
  width: 100%;

  /* 左边的数字输入框 */
  .input-left {
    flex: 1;
    min-width: 0;

    :deep(.t-input) {
      border-top-right-radius: 0 !important;
      border-bottom-right-radius: 0 !important;
      border-right: none !important;
      padding: 0 !important;
    }

    /* 数字绝对居中 */
    :deep(.t-input__inner) {
      text-align: center !important;
    }
  }

  /* 右边的单位选择器 */
  .select-right {
    width: 40px !important;
    flex-shrink: 0;

    :deep(.t-input) {
      border-top-left-radius: 0 !important;
      border-bottom-left-radius: 0 !important;
      background-color: var(--td-bg-color-secondarycontainer) !important;
      padding: 0 !important;
    }

    :deep(.t-input__inner) {
      text-align: center !important;
      padding: 0 !important;
      font-size: 12px !important;
      color: var(--td-text-color-secondary) !important;
    }

    /* 隐藏下拉箭头 */
    :deep(.t-select__right-icon) {
      display: none !important;
    }
  }
}
</style>
