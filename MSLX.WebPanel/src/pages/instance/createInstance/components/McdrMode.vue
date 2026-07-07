<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

import ServerCoreSelector from './ServerCoreSelector.vue';
import { getJavaVersionList } from '@/api/mslapi/java';
import { getLocalJavaList } from '@/api/localJava';
import { getPythonList } from '@/api/localPython';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { deleteUpload } from '@/api/files';
import { CreateInstanceQucikModeModel, PythonInfoModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { useInstanceListStore } from '@/store/modules/instance';
import { useFileUpload } from '@/hooks/useFileUpload';

const { isUploading, uploadProgress, uploadedFileName, uploadedFileSize, startUpload, removeUploadData } =
  useFileUpload();

const userStore = useUserStore();
const instanceListstore = useInstanceListStore();
const formRef = ref(null);

// 创建流程状态
const isSubmitting = ref(false);
const isCreating = ref(false);
const isSuccess = ref(false);
const progress = ref(0);
const statusMessages = ref<{ time: string; message: string; progress: number | null }[]>([]);
const hubConnection = ref<HubConnection | null>(null);
const createdServerId = ref<string | null>(null);
const logContainerRef = ref<HTMLDivElement | null>(null);

// Python 环境
const pythonType = ref<'detected' | 'custom'>('detected');
const pythonList = ref<PythonInfoModel[]>([]);
const detectedPython = ref('');
const customPython = ref('');
const isScanningPython = ref(false);

const selectedPythonInfo = computed(() =>
  pythonList.value.find((p) => p.path === detectedPython.value),
);

const fetchPython = async (force = false) => {
  isScanningPython.value = true;
  try {
    const res = await getPythonList(force);
    pythonList.value = Array.isArray(res) ? res : [];
    if (pythonList.value.length > 0 && !detectedPython.value) {
      // 优先选已安装 MCDR 的
      const withMcdr = pythonList.value.find((p) => p.hasMcdr);
      detectedPython.value = (withMcdr ?? pythonList.value[0]).path;
    }
    if (pythonList.value.length === 0) {
      pythonType.value = 'custom';
    }
  } catch (e: any) {
    MessagePlugin.warning('扫描 Python 失败：' + e.message);
    pythonType.value = 'custom';
  } finally {
    isScanningPython.value = false;
  }
};

// Java 环境(内部 MC 服务端使用)
const javaType = ref('online');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersions = ref<{ label: string; value: string }[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');

const fetchJavaVersions = async (force = false) => {
  try {
    const res = await getJavaVersionList(
      userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', ''),
      userStore.userInfo.systemInfo.osArchitecture.toLowerCase(),
    );
    if (res && Array.isArray(res)) {
      javaVersions.value = res.map((v) => ({ label: `Java ${v}`, value: v }));
      if (javaVersions.value.length > 1 && !selectedJavaVersion.value) {
        selectedJavaVersion.value = javaVersions.value[1].value;
      }
    }
    localJavaVersions.value = (await getLocalJavaList(force)).map((v) => ({
      label: `Java ${v.version}${v.is64Bit ? '' : ' (32位)'} (${v.vendor} | ${v.path})`,
      value: v.path,
    }));
    if (localJavaVersions.value.length > 0) {
      customJavaPath.value = localJavaVersions.value[0].value;
    }
  } catch (e: any) {
    MessagePlugin.warning('获取 Java 版本失败：' + e.message);
  }
};

onMounted(() => {
  fetchPython();
  fetchJavaVersions();
});

// 核心选择
const downloadType = ref<'online' | 'manual' | 'custom'>('online');
const showCoreSelector = ref(false);
const uploadInputRef = ref<HTMLInputElement | null>(null);

// handler
const handlerType = ref<'auto' | 'manual'>('auto');
const handlerOptions = [
  { label: 'vanilla_handler (原版 / Fabric / Carpet)', value: 'vanilla_handler' },
  { label: 'bukkit_handler (Paper / Spigot / Mohist / Folia)', value: 'bukkit_handler' },
  { label: 'bukkit14_handler (Bukkit / Spigot 1.14+)', value: 'bukkit14_handler' },
  { label: 'forge_handler (Forge / NeoForge)', value: 'forge_handler' },
  { label: 'arclight_handler (Arclight)', value: 'arclight_handler' },
  { label: 'cat_server_handler (CatServer)', value: 'cat_server_handler' },
  { label: 'bungeecord_handler (BungeeCord)', value: 'bungeecord_handler' },
  { label: 'waterfall_handler (Waterfall)', value: 'waterfall_handler' },
  { label: 'velocity_handler (Velocity)', value: 'velocity_handler' },
];

const formData = ref<CreateInstanceQucikModeModel>({
  name: '新建 MCDR 服务器',
  path: '',
  java: '',
  core: '',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
  packageFileKey: '',
  packageLocalPath: '',
  minM: 1024,
  maxM: 4096,
  args: '',
  mcdr: true,
  mcdrPython: 'python',
  mcdrHandler: '',
  mcdrInstall: true,
  mcdrPipMirror: 'https://pypi.tuna.tsinghua.edu.cn/simple',
});

// 内存单位换算
const unitOptions = [
  { label: 'GB', value: 'GB' },
  { label: 'MB', value: 'MB' },
];
const minUnit = ref('GB');
const maxUnit = ref('GB');
const minMComputed = computed({
  get: () => (minUnit.value === 'GB' ? formData.value.minM / 1024 : formData.value.minM),
  set: (val) => {
    formData.value.minM = minUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});
const maxMComputed = computed({
  get: () => (maxUnit.value === 'GB' ? formData.value.maxM / 1024 : formData.value.maxM),
  set: (val) => {
    formData.value.maxM = maxUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});

// 同步 Python 到 formData
watch(
  [pythonType, detectedPython, customPython],
  ([type, detected, custom]) => {
    formData.value.mcdrPython = type === 'custom' ? custom || 'python' : detected || 'python';
  },
  { immediate: true },
);

// 同步 Java 到 formData
watch(
  [javaType, selectedJavaVersion, customJavaPath],
  ([type, ver, path]) => {
    if (type === 'env') formData.value.java = 'java';
    else if (type === 'custom' || type === 'local') formData.value.java = path;
    else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';
  },
  { immediate: true },
);

const rules = computed<FormRules>(() => ({
  name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
  java: [{ required: true, message: '请配置内部服务端的 Java 环境', trigger: 'change' }],
}));

// 上传核心
const triggerFileSelect = () => uploadInputRef.value?.click();

const onFileChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;

  if (formData.value.coreFileKey) {
    try {
      await deleteUpload(formData.value.coreFileKey);
    } catch (e) {
      console.warn('清理旧文件失败', e);
    }
  }

  const file = input.files[0];
  formData.value.core = file.name;
  formData.value.coreUrl = '';
  formData.value.coreSha256 = '';
  formData.value.coreFileKey = '';

  try {
    formData.value.coreFileKey = await startUpload(file);
    MessagePlugin.success('核心文件上传成功！');
  } catch (error: any) {
    if (error.message !== '已取消') {
      MessagePlugin.error(`上传失败: ${error.message || '未知错误'}`);
      formData.value.core = '';
    }
  } finally {
    input.value = '';
  }
};

const removeUploadedFile = async () => {
  if (formData.value.coreFileKey) {
    await removeUploadData();
    formData.value.coreFileKey = '';
    formData.value.core = '';
    MessagePlugin.success('文件已移除');
  }
};

const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = '';
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);
};

// 提交
const onSubmit = async () => {
  const validateResult = await formRef.value.validate();
  if (validateResult !== true) {
    MessagePlugin.warning('请检查表单填写');
    return;
  }

  if (downloadType.value === 'online' && !formData.value.coreUrl && !formData.value.core) {
    MessagePlugin.warning('请选择一个服务端核心');
    return;
  }
  if (downloadType.value === 'manual' && !formData.value.coreFileKey) {
    MessagePlugin.warning('请先上传核心文件');
    return;
  }
  if (downloadType.value === 'custom' && !formData.value.core) {
    MessagePlugin.warning('请输入核心文件名');
    return;
  }

  isSubmitting.value = true;
  statusMessages.value = [];

  const apiData = {
    ...formData.value,
    path: formData.value.path || null,
    coreUrl: downloadType.value === 'online' ? formData.value.coreUrl || null : null,
    coreSha256: downloadType.value === 'online' ? formData.value.coreSha256 || null : null,
    coreFileKey: downloadType.value === 'manual' ? formData.value.coreFileKey || null : null,
    args: formData.value.args || null,
    mcdr: true,
    mcdrHandler: handlerType.value === 'manual' ? formData.value.mcdrHandler : '',
  };

  try {
    const response = await postCreateInstanceQuickMode(apiData);
    const serverId = response.serverId;
    if (!serverId) throw new Error('服务器未返回 ServerId');

    createdServerId.value = serverId.toString();
    isCreating.value = true;
    await startSignalRConnection(createdServerId.value);
  } catch (error: any) {
    MessagePlugin.error(error.message || '创建请求失败');
    isSubmitting.value = false;
  }
};

const startSignalRConnection = async (serverId: string) => {
  const { baseUrl, token } = userStore;
  let isSuccessHandled = false;

  const hubUrl = new URL('/api/hubs/creationProgressHub', baseUrl || window.location.origin);
  hubUrl.searchParams.append('x-user-token', token);

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Information)
    .build();

  const addLog = (message: string, prog: number | null = null) => {
    statusMessages.value.push({ time: new Date().toLocaleTimeString(), message, progress: prog });
    nextTick(() => {
      if (logContainerRef.value) logContainerRef.value.scrollTop = logContainerRef.value.scrollHeight;
    });
  };

  hubConnection.value.on('StatusUpdate', (id, message, prog) => {
    if (id.toString() !== serverId) return;
    addLog(message, prog);
    if (prog !== null && prog >= 0) progress.value = prog;

    if (prog === 100) {
      isSuccessHandled = true;
      MessagePlugin.success('MCDR 服务器创建成功！');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSuccess.value = true;
      isSubmitting.value = false;
      instanceListstore.refreshInstanceList();
    } else if (prog === -1) {
      MessagePlugin.error(message || '创建过程中发生错误');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSubmitting.value = false;
    }
  });

  try {
    await hubConnection.value.start();
    addLog('已连接到实时进度服务...');
    await hubConnection.value.invoke('TrackServer', serverId);
    addLog('已订阅任务，等待服务器响应...');
  } catch (err: any) {
    if (!isSuccessHandled) {
      addLog(`SignalR 连接失败: ${err.message}`, -1);
      MessagePlugin.error('无法连接到实时进度服务');
      isCreating.value = false;
      isSubmitting.value = false;
    }
  }
};

onUnmounted(() => {
  hubConnection.value?.stop();
});

const goToHome = () => {
  isSuccess.value = false;
  isCreating.value = false;
  progress.value = 0;
  formData.value = {
    ...formData.value,
    name: '新建 MCDR 服务器',
    core: '',
    coreUrl: '',
    coreSha256: '',
    coreFileKey: '',
    path: '',
    args: '',
  };
  removeUploadData();
};
</script>

<template>
  <div
    class="design-card list-item-anim bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-sm p-6 sm:p-8"
  >
    <!-- 表单 -->
    <div v-if="!isCreating && !isSuccess" class="flex flex-col relative pt-1">
      <t-alert theme="info" class="!mb-6 !rounded-xl">
        <template #message>
          <div class="text-sm leading-relaxed">
            <b>MCDReforged (MCDR)</b> 是基于 Python 的服务端包装器。此模式会自动搭建
            <code class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded">MCDR + 真实服务端</code>
            的目录结构(真实服务端位于 <code class="font-mono">server/</code> 子目录),并生成 config.yml。
            需要本机已安装 <b>Python 3.8+</b>。
          </div>
        </template>
      </t-alert>

      <t-form
        ref="formRef"
        :rules="rules"
        :data="formData"
        label-align="top"
        class="flex-1 flex flex-col [&_.t-form__item]:!mb-6"
        @submit="onSubmit"
      >
        <!-- 基本信息 -->
        <t-form-item label="实例名称" name="name">
          <t-input v-model="formData.name" placeholder="给你的 MCDR 服务器起个名字" class="!w-full sm:!w-[28rem]" />
        </t-form-item>

        <t-form-item
          label="存储路径 (可选)"
          name="path"
          :help="
            userStore.userInfo.systemInfo.docker
              ? '您正在使用 Docker 部署，仅支持默认数据路径'
              : '选填，留空使用默认路径'
          "
        >
          <t-input
            v-model="formData.path"
            :disabled="userStore.userInfo.systemInfo.docker"
            placeholder="例如: D:\MyMCDRServer"
            class="!w-full sm:!w-[28rem] !font-mono"
          />
        </t-form-item>

        <!-- Python 环境 -->
        <t-form-item label="Python 环境 (运行 MCDR)">
          <div class="w-full">
            <t-radio-group v-model="pythonType" variant="default-filled" class="!mb-4">
              <t-radio-button value="detected">已检测到的 Python</t-radio-button>
              <t-radio-button value="custom">自定义命令 / 路径</t-radio-button>
            </t-radio-group>

            <div class="w-full sm:w-[32rem]">
              <div v-if="pythonType === 'detected'" class="flex items-center gap-3">
                <t-select
                  v-model="detectedPython"
                  :options="pythonList.map((p) => ({
                    label: `${p.path}  ·  Python ${p.version}${p.hasMcdr ? ` · MCDR ${p.mcdrVersion}` : ' · 未装 MCDR'}`,
                    value: p.path,
                  }))"
                  :loading="isScanningPython"
                  placeholder="未检测到 Python，请切换到自定义"
                  class="!flex-1"
                />
                <t-button variant="outline" theme="primary" :loading="isScanningPython" @click="fetchPython(true)"
                  >重新扫描</t-button
                >
              </div>

              <div v-else>
                <t-input
                  v-model="customPython"
                  placeholder="例如: python3 或 C:\Python312\python.exe"
                  class="!font-mono"
                />
              </div>

              <div
                v-if="pythonType === 'detected' && selectedPythonInfo && !selectedPythonInfo.hasMcdr"
                class="text-[11px] text-amber-600 dark:text-amber-400 mt-2 font-medium"
              >
                ⚠ 该 Python 尚未安装 MCDReforged，创建时将自动通过 pip 安装。
              </div>
            </div>
          </div>
        </t-form-item>

        <!-- 服务端核心 -->
        <t-form-item label="内部服务端核心">
          <div class="w-full">
            <t-radio-group v-model="downloadType" variant="default-filled" class="!mb-4">
              <t-radio-button value="online">在线下载 (推荐)</t-radio-button>
              <t-radio-button value="manual">上传本地文件</t-radio-button>
              <t-radio-button value="custom">自定义文件名</t-radio-button>
            </t-radio-group>

            <div class="w-full sm:w-[32rem]">
              <div v-if="downloadType === 'online'">
                <t-button
                  variant="outline"
                  class="!w-full !justify-start !pl-4 !h-10 !bg-transparent hover:!border-[var(--color-primary)]"
                  @click="showCoreSelector = true"
                >
                  <template #icon><t-icon name="cloud-download" class="opacity-70" /></template>
                  点击打开服务端核心选择库
                </t-button>
                <div
                  v-if="formData.core"
                  class="flex items-center gap-3 mt-4 p-3 rounded-lg border border-[var(--color-primary)]/40"
                >
                  <t-icon name="check-circle-filled" class="text-[var(--color-primary)] text-xl shrink-0" />
                  <div class="flex-1 min-w-0">
                    <div class="font-bold text-sm truncate">{{ formData.core }}</div>
                    <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                      将部署到 server/ 子目录
                    </div>
                  </div>
                </div>
              </div>

              <div v-if="downloadType === 'manual'">
                <input ref="uploadInputRef" accept=".jar" type="file" style="display: none" @change="onFileChange" />
                <t-button
                  v-if="!isUploading && !formData.coreFileKey"
                  variant="outline"
                  class="!w-full !justify-start !pl-4 !h-10 !bg-transparent hover:!border-[var(--color-primary)]"
                  @click="triggerFileSelect"
                >
                  <template #icon><t-icon name="upload" class="opacity-70" /></template>
                  点击选择文件并上传 (.jar)
                </t-button>
                <div v-if="isUploading" class="w-full p-4 mt-4 rounded-lg border border-[var(--color-primary)]/40">
                  <div class="text-sm font-bold mb-2 truncate">正在上传: {{ uploadedFileName }} ({{ uploadedFileSize }})</div>
                  <t-progress theme="line" :percentage="uploadProgress" :label="`${Math.round(uploadProgress)}%`" />
                </div>
                <div
                  v-if="formData.coreFileKey && !isUploading"
                  class="flex items-center gap-3 mt-4 p-3 rounded-lg border border-[var(--color-success)]/40"
                >
                  <t-icon name="check-circle-filled" class="text-[var(--color-success)] text-xl shrink-0" />
                  <div class="flex-1 min-w-0">
                    <div class="font-bold text-sm truncate">{{ uploadedFileName }}</div>
                    <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                      {{ uploadedFileSize }} | 已就绪
                    </div>
                  </div>
                  <t-button shape="square" variant="text" theme="danger" @click="removeUploadedFile">
                    <t-icon name="delete" />
                  </t-button>
                </div>
              </div>

              <div v-if="downloadType === 'custom'">
                <t-input v-model="formData.core" placeholder="请输入核心文件名，例如: server.jar" class="!font-mono" />
                <div class="text-[11px] text-zinc-500 mt-1">用于稍后手动放入 server/ 目录的核心文件名。</div>
              </div>
            </div>
          </div>
        </t-form-item>

        <!-- Java 环境 -->
        <t-form-item label="内部服务端 Java 环境" name="java">
          <div class="w-full">
            <t-radio-group v-model="javaType" variant="default-filled" class="!mb-4">
              <t-radio-button value="online">在线下载</t-radio-button>
              <t-radio-button value="local">选择本机 Java</t-radio-button>
              <t-radio-button value="env">环境变量</t-radio-button>
              <t-radio-button value="custom">自定义路径</t-radio-button>
            </t-radio-group>
            <div class="w-full sm:w-[32rem]">
              <t-select
                v-if="javaType === 'online'"
                v-model="selectedJavaVersion"
                :options="javaVersions"
                placeholder="请选择 Java 版本"
                class="!w-full sm:!w-64"
              />
              <div v-else-if="javaType === 'local'" class="flex items-center gap-3">
                <t-select v-model="customJavaPath" :options="localJavaVersions" placeholder="请选择 Java" class="!flex-1" />
                <t-button variant="outline" theme="primary" @click="fetchJavaVersions(true)">重新扫描</t-button>
              </div>
              <t-input v-else-if="javaType === 'env'" model-value="java" readonly disabled class="!font-mono" />
              <t-input
                v-else
                v-model="customJavaPath"
                placeholder="例如: C:\Program Files\Java\jdk-21\bin\java.exe"
                class="!font-mono"
              />
            </div>
          </div>
        </t-form-item>

        <!-- 内存 -->
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-6 w-full sm:w-[40rem]">
          <t-form-item label="最小内存" name="minM" class="!mb-0">
            <div class="flex items-center gap-2 w-full">
              <t-input-number
                v-model="minMComputed"
                :min="0"
                :decimal-places="minUnit === 'GB' ? 1 : 0"
                theme="column"
                class="!w-full"
              />
              <t-select v-model="minUnit" :options="unitOptions" :clearable="false" class="!w-20 shrink-0" />
            </div>
          </t-form-item>
          <t-form-item label="最大内存" name="maxM" class="!mb-0">
            <div class="flex items-center gap-2 w-full">
              <t-input-number
                v-model="maxMComputed"
                :min="0"
                :decimal-places="maxUnit === 'GB' ? 1 : 0"
                theme="column"
                class="!w-full"
              />
              <t-select v-model="maxUnit" :options="unitOptions" :clearable="false" class="!w-20 shrink-0" />
            </div>
          </t-form-item>
        </div>

        <!-- 高级选项 -->
        <t-collapse :default-value="[]" class="!mt-6 !bg-transparent !border-none">
          <t-collapse-panel value="adv" header="高级选项 (Handler / MCDR 安装 / 镜像源)">
            <t-form-item label="MCDR Handler" class="!mb-6">
              <div class="w-full">
                <t-radio-group v-model="handlerType" variant="default-filled" class="!mb-3">
                  <t-radio-button value="auto">自动推断 (推荐)</t-radio-button>
                  <t-radio-button value="manual">手动指定</t-radio-button>
                </t-radio-group>
                <t-select
                  v-if="handlerType === 'manual'"
                  v-model="formData.mcdrHandler"
                  :options="handlerOptions"
                  placeholder="请选择 handler"
                  class="!w-full sm:!w-[32rem]"
                />
                <div v-else class="text-[11px] text-zinc-500">将根据核心文件名自动选择合适的 handler。</div>
              </div>
            </t-form-item>

            <t-form-item label="额外 JVM 参数 (可选)" class="!mb-6">
              <t-textarea
                v-model="formData.args"
                placeholder="-XX:+UseG1GC"
                :autosize="{ minRows: 2, maxRows: 5 }"
                class="!font-mono !bg-transparent"
              />
            </t-form-item>

            <t-form-item label="自动安装 MCDReforged" class="!mb-6">
              <div class="flex items-center gap-3">
                <t-switch v-model="formData.mcdrInstall" size="large" />
                <span class="text-sm">{{ formData.mcdrInstall ? '创建时自动 pip 安装' : '不自动安装(需自行安装)' }}</span>
              </div>
            </t-form-item>

            <t-form-item label="pip 镜像源 (可选)" class="!mb-0">
              <t-input
                v-model="formData.mcdrPipMirror"
                placeholder="留空使用默认源"
                class="!font-mono !w-full sm:!w-[32rem]"
              />
            </t-form-item>
          </t-collapse-panel>
        </t-collapse>

        <div class="mt-6 pt-6 border-t border-zinc-200 dark:border-zinc-700">
          <t-button theme="primary" type="submit" :loading="isSubmitting" class="!rounded-xl !font-bold !h-11 !px-8"
            >提交创建</t-button
          >
        </div>
      </t-form>
    </div>

    <!-- 创建进度 -->
    <div v-if="isCreating" class="h-full flex flex-col items-center justify-center py-8 list-item-anim">
      <div class="text-lg font-bold mb-2">正在创建 MCDR 实例 ({{ createdServerId }})</div>
      <p class="text-sm text-[var(--td-text-color-secondary)] mb-6">请勿关闭此页面，安装 MCDR 与下载核心可能需要几分钟...</p>
      <div class="w-full max-w-lg !my-6">
        <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(0)}%`" />
      </div>
      <div class="w-full max-w-2xl bg-white/40 dark:bg-zinc-900/40 rounded-2xl border border-white/60 dark:border-zinc-700/50 p-4 h-64 flex flex-col mt-6">
        <div ref="logContainerRef" class="flex-1 overflow-y-auto pr-2">
          <div v-for="(log, index) in statusMessages" :key="index" class="text-xs font-mono mb-2 leading-relaxed">
            <span class="text-[var(--td-text-color-secondary)] mr-2">[{{ log.time }}]</span>
            <span class="text-[var(--td-text-color-primary)] font-medium">{{ log.message }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 成功 -->
    <div v-if="isSuccess" class="flex flex-col items-center justify-center py-8 min-h-[40vh] list-item-anim">
      <t-icon name="check-circle" size="64px" class="text-[var(--color-success)]" />
      <div class="text-xl text-center font-medium !mt-4">MCDR 服务器 ({{ createdServerId }}) 已创建成功</div>
      <div class="text-sm text-[var(--td-text-color-secondary)] !my-2 !mb-8">首次启动时如提示 EULA，请在面板同意后继续。</div>
      <div class="flex gap-4">
        <t-button @click="() => { goToHome(); changeUrl('/instance/list'); }">返回服务端列表</t-button>
        <t-button theme="default" @click="() => { goToHome(); changeUrl(`/instance/console/${createdServerId}`); }"
          >前往控制台</t-button
        >
      </div>
    </div>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}
@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
