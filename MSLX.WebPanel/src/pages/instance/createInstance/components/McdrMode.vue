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

// 状态管理
const userStore = useUserStore();
const formRef = ref(null);

const instanceListstore = useInstanceListStore();

const currentStep = ref(0);
const isSubmitting = ref(false);

const isCreating = ref(false);
const isSuccess = ref(false);
const progress = ref(0);
const statusMessages = ref<{ time: string; message: string; progress: number | null }[]>([]);
const hubConnection = ref<HubConnection | null>(null);
const createdServerId = ref<string | null>(null);
const logContainerRef = ref<HTMLDivElement | null>(null); // 日志容器dom

// 核心选择相关状态
const downloadType = ref<'online' | 'manual' | 'custom'>('online'); // 'online' | 'manual' | 'custom'
const showCoreSelector = ref(false);

// Input DOM 引用
const uploadInputRef = ref<HTMLInputElement | null>(null);

// Python 相关状态
const pythonType = ref<'detected' | 'custom'>('detected');
const pythonList = ref<PythonInfoModel[]>([]);
const detectedPython = ref('');
const customPython = ref('');
const isScanningPython = ref(false);

const selectedPythonInfo = computed(() => pythonList.value.find((p) => p.path === detectedPython.value));

const fetchPython = async (force: boolean = false) => {
  isScanningPython.value = true;
  try {
    if (force) {
      MessagePlugin.info('正在刷新 Python 列表(重新扫描耗时较长)...');
    }
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
    if (force) {
      MessagePlugin.success('已刷新 Python 列表');
    }
  } catch (e: any) {
    MessagePlugin.warning('扫描 Python 失败：' + e.message);
    pythonType.value = 'custom';
  } finally {
    isScanningPython.value = false;
  }
};

// Java 选择相关状态
const javaType = ref('online');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersions = ref<{ label: string; value: string }[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');

// Java 版本推荐相关状态
const onlineGameVersion = ref('');
const recommendedJavaVersion = ref('');
const detectedGameVersion = ref('');

// 尝试解析版本号
const parseMcVersion = (input: string): number[] | null => {
  const m = input.match(/(\d+)\.(\d+)(?:\.(\d+))?/);
  if (!m) return null;
  return [parseInt(m[1]), parseInt(m[2]), parseInt(m[3] ?? '0')];
};

// 判断Java推荐
const getRecommendedJavaFromParsed = (ver: number[]): string => {
  const [major, minor, patch] = ver;
  // MC 26+
  if (major >= 26) return '25';
  if (major === 1) {
    // 1.20.5 - 1.21.x => Java 21
    if ((minor === 20 && patch >= 5) || (minor >= 21)) return '21';
    // 1.18 - 1.20.4 => Java 17
    if (minor >= 18) return '17';
    // 1.17.x => Java 16
    if (minor === 17) return '16';
    // 1.13 及以下 => Java 8
    return '8';
  }
  return '25';
};

const javaVersionsWithOptions = computed(() =>
  javaVersions.value.map((opt) => ({
    ...opt,
    label: recommendedJavaVersion.value && opt.value === recommendedJavaVersion.value
      ? `${opt.label} (推荐)`
      : opt.label,
  }))
);

const localJavaVersionsRaw = ref<{ label: string; value: string; javaVer: string }[]>([]);
const localJavaVersionsWithOptions = computed(() =>
  localJavaVersionsRaw.value.map((opt) => ({
    ...opt,
    label: recommendedJavaVersion.value && opt.javaVer === recommendedJavaVersion.value
      ? `${opt.label} (推荐)`
      : opt.label,
  }))
);

const fetchJavaVersions = async (force: boolean = false) => {
  try {
    if (force) {
      MessagePlugin.info('正在刷新Java版本列表(重新扫描耗时较长)...');
    }
    const res = await getJavaVersionList(
      userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', ''),
      userStore.userInfo.systemInfo.osArchitecture.toLowerCase(),
    );
    if (res && Array.isArray(res)) {
      javaVersions.value = res.map((v) => ({ label: `Java ${v}`, value: v }));
      if (javaVersions.value.length > 0 && !selectedJavaVersion.value) {
        selectedJavaVersion.value = javaVersions.value[0].value; // 默认第一项
      }
    }
    const rawLocal = await getLocalJavaList(force);
    localJavaVersionsRaw.value = rawLocal.map((v) => ({
      label: `Java ${v.version}${v.is64Bit ? '' : ' (32位)'} (${v.vendor} | ${v.path})`,
      value: v.path,
      javaVer: String(v.version).split('.')[0],
    }));
    localJavaVersions.value = localJavaVersionsRaw.value.map(({ label, value }) => ({ label, value }));
    if (localJavaVersions.value.length > 0) {
      customJavaPath.value = localJavaVersions.value[0].value;
    }
    if (force) {
      MessagePlugin.success('已刷新Java版本列表');
    }
  } catch (e: any) {
    MessagePlugin.warning('获取在线Java版本失败' + e.message);
  }
};

onMounted(() => {
  fetchPython();
  fetchJavaVersions();
});

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

const formData = ref(<CreateInstanceQucikModeModel>{
  name: '新建 MCDR 服务器',
  path: '',
  java: '',
  core: '',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '', // 上传成功后的 Key
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

// 处理内存的单位
const unitOptions = [
  { label: 'GB', value: 'GB' },
  { label: 'MB', value: 'MB' },
];

const minUnit = ref('GB');
const maxUnit = ref('GB');

const minMComputed = computed({
  get: () => {
    return minUnit.value === 'GB' ? formData.value.minM / 1024 : formData.value.minM;
  },
  set: (val) => {
    formData.value.minM = minUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});

const maxMComputed = computed({
  get: () => {
    return maxUnit.value === 'GB' ? formData.value.maxM / 1024 : formData.value.maxM;
  },
  set: (val) => {
    formData.value.maxM = maxUnit.value === 'GB' ? Math.round(val * 1024) : val;
  },
});

// 监听选择 Python 的状态变量 修改表单数据
watch(
  [pythonType, detectedPython, customPython],
  ([type, detected, custom]) => {
    formData.value.mcdrPython = type === 'custom' ? custom || 'python' : detected || 'python';
    if (formData.value.mcdrPython) {
      formRef.value?.validate({ fields: ['mcdrPython'] });
    }
  },
  { immediate: true },
);

// 监听选择java的状态变量 修改表单数据
watch(
  [javaType, selectedJavaVersion, customJavaPath],
  ([type, ver, path]) => {
    if (type === 'env') {
      formData.value.java = 'java';
    } else if (type === 'custom') {
      formData.value.java = path;
    } else if (type === 'local') {
      formData.value.java = path;
    } else if (type === 'online') {
      formData.value.java = ver ? `MSLX://Java/${ver}` : '';
    }

    if (formData.value.java) {
      formRef.value?.validate({ fields: ['java'] });
    }
  },
  { immediate: true },
);

// 表单校验规则
const FORM_RULES = computed<FormRules>(() => {
  return {
    name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
    mcdrPython: [
      {
        validator: () => {
          if (pythonType.value === 'custom' && !customPython.value) {
            return { result: false, message: '请填写 Python 命令或路径', type: 'error' };
          }
          if (pythonType.value === 'detected' && !detectedPython.value) {
            return { result: false, message: '未检测到可用的 Python，请切换到自定义', type: 'error' };
          }
          return true;
        },
        trigger: 'change',
      },
    ],
    java: [{ required: true, message: '请配置内部服务端 Java 环境', trigger: 'change' }],
    core: [{ required: true, message: '核心名称/文件不能为空', trigger: 'change' }],
    coreUrl: [
      {
        validator: (val) => {
          if (downloadType.value === 'online') {
            if (!val) return { result: false, message: '请选择一个服务端核心', type: 'error' };
            if (val && !/^https?:\/\/.+/.test(val))
              return { result: false, message: '下载地址必须以 http(s) 开头', type: 'error' };
          }
          return true;
        },
        trigger: 'change',
      },
    ],
    coreFileKey: [
      {
        validator: (val) => {
          if (downloadType.value === 'manual' && !val) {
            return { result: false, message: '请上传核心文件', type: 'error' };
          }
          return true;
        },
        trigger: 'change',
      },
    ],
    minM: [{ required: true, min: 1, message: '最小内存必须大于0', trigger: 'blur' }],
    maxM: [{ required: true, min: 1, message: '最大内存必须大于0', trigger: 'blur' }],
  };
});

const stepValidationFields = [
  ['name', 'path'],
  ['mcdrPython'],
  ['core', 'coreUrl', 'coreSha256', 'coreFileKey'],
  ['java'],
  ['minM', 'maxM', 'args'],
  [],
];

// 步骤导航
const prevStep = () => {
  if (currentStep.value > 0) {
    currentStep.value -= 1;
  }
};

const nextStep = async () => {
  if (currentStep.value === 2) {
    if (downloadType.value === 'online') {
      if (!formData.value.coreUrl || !formData.value.core) {
        MessagePlugin.warning('请点击按钮选择一个服务端核心');
        return;
      }
    } else if (downloadType.value === 'custom') {
      if (!formData.value.core) {
        MessagePlugin.warning('请输入核心文件名');
        return;
      }
    } else {
      if (!formData.value.coreFileKey) {
        MessagePlugin.warning('请先上传核心文件');
        return;
      }
    }
  }

  const validateResult = await formRef.value.validate();
  if (validateResult === true) {
    if (currentStep.value < 5) currentStep.value += 1;
    return;
  }

  const fieldsToValidate = new Set(stepValidationFields[currentStep.value]);
  const hasErrorInCurrentStep = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasErrorInCurrentStep) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    if (currentStep.value < 5) {
      currentStep.value += 1;
    }
  }
};

const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = '';
  onlineGameVersion.value = data.version || '';
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);

  formRef.value.validate({ fields: ['core', 'coreUrl'] });
};

// --- 上传文件 ---
const triggerFileSelect = () => {
  uploadInputRef.value?.click();
};

const onFileChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;

  // 删除旧临时文件
  if (formData.value.coreFileKey) {
    try {
      await deleteUpload(formData.value.coreFileKey);
      console.log('旧临时文件已清理:', formData.value.coreFileKey);
    } catch (e) {
      console.warn('清理旧文件失败，可能文件已过期', e);
    }
  }

  const file = input.files[0];

  // 预重置表单相关状态
  formData.value.core = file.name;
  formData.value.coreUrl = '';
  formData.value.coreSha256 = '';
  formData.value.coreFileKey = '';

  try {
    const finalKey = await startUpload(file);
    formData.value.coreFileKey = finalKey;
    MessagePlugin.success('核心文件上传成功！');

    // 触发单独校验
    formRef.value?.validate({ fields: ['core', 'coreFileKey'] });
  } catch (error: any) {
    if (error.message !== '已取消') {
      console.error(error);
      MessagePlugin.error(`上传失败: ${error.message || '未知错误'}`);
      formData.value.core = '';
    }
  } finally {
    input.value = '';
  }
};

const removeUploadedFile = async () => {
  if (formData.value.coreFileKey) {
    // 清理云端暂存文件
    await removeUploadData();
    formData.value.coreFileKey = '';
    formData.value.core = '';
    MessagePlugin.success('文件已移除');
  }
};
// --- 上传相关 ---

// 提交 & SignalR 状态
const onSubmit = async () => {
  const validateResult = await formRef.value.validate();
  const isValid = validateResult === true;

  if (!isValid) {
    MessagePlugin.warning('请检查表单所有内容');
    return;
  }

  isSubmitting.value = true;
  statusMessages.value = [];

  const apiData = {
    ...formData.value,
    path: formData.value.path || null,
    coreUrl: formData.value.coreUrl || null,
    coreSha256: formData.value.coreSha256 || null,
    coreFileKey: formData.value.coreFileKey || null,
    args: formData.value.args || null,
    mcdr: true,
    mcdrHandler: handlerType.value === 'manual' ? formData.value.mcdrHandler : '',
  };

  if (downloadType.value === 'manual') {
    apiData.coreUrl = null;
    apiData.coreSha256 = null;
  } else {
    apiData.coreFileKey = null;
  }

  try {
    const response = await postCreateInstanceQuickMode(apiData);
    const serverId = response.serverId;
    if (!serverId) {
      throw new Error('服务器未返回 ServerId');
    }

    createdServerId.value = serverId.toString();
    isCreating.value = true;
    currentStep.value = 6;

    await startSignalRConnection(createdServerId.value);
  } catch (error: any) {
    const errorMessage = error.message || '创建请求失败，请检查网络或后端服务';
    MessagePlugin.error(errorMessage);
    isSubmitting.value = false;
  }
};

// SignalR 主要链接方法
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
    statusMessages.value.push({
      time: new Date().toLocaleTimeString(),
      message,
      progress: prog,
    });

    nextTick(() => {
      if (logContainerRef.value) {
        logContainerRef.value.scrollTop = logContainerRef.value.scrollHeight;
      }
    });
  };

  hubConnection.value.on('StatusUpdate', (id, message, prog) => {
    if (id.toString() !== serverId) return;

    addLog(message, prog);

    if (prog !== null && prog >= 0) {
      progress.value = prog;
    }

    if (prog === 100) {
      isSuccessHandled = true;
      MessagePlugin.success('MCDR 服务器创建成功！');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSuccess.value = true;
      currentStep.value = 7;
      isSubmitting.value = false;
      instanceListstore.refreshInstanceList();
    } else if (prog === -1) {
      MessagePlugin.error(message || '创建过程中发生未知错误');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSubmitting.value = false;
      currentStep.value = 0;
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
      currentStep.value = 0;
    }
  }
};

onUnmounted(() => {
  hubConnection.value?.stop();
  // 移除了组件内生硬的局部上传中断控制，交给 Hook 内部自动进行生命周期熔断守护
});

const goToHome = () => {
  isSuccess.value = false;
  currentStep.value = 0;
  formData.value = {
    ...formData.value,
    name: '新建 MCDR 服务器',
    core: '',
    coreUrl: '',
    coreSha256: '',
    path: '',
    args: '',
    coreFileKey: '',
  };
  downloadType.value = 'online';
  javaType.value = 'online';
  customJavaPath.value = '';
  onlineGameVersion.value = '';
  recommendedJavaVersion.value = '';
  detectedGameVersion.value = '';
  pythonType.value = pythonList.value.length > 0 ? 'detected' : 'custom';
  handlerType.value = 'auto';

  // 视图重置时同步清理 hook 历史状态
  removeUploadData();
};

// Docker 镜像预设版本
const dockerImagePresetVersion = ref('25');

const updateJavaSelectionByRecommendation = () => {
  let gameVerStr = '';
  let parsed: number[] | null = null;

  // 优先使用在线选择器提供的精确版本号
  if (onlineGameVersion.value) {
    gameVerStr = onlineGameVersion.value;
    parsed = parseMcVersion(gameVerStr);
  } else if (formData.value.core) {
    // 尝试从文件名解析
    gameVerStr = formData.value.core;
    parsed = parseMcVersion(gameVerStr);
  }

  if (!parsed) {
    // 无法识别
    recommendedJavaVersion.value = '';
    detectedGameVersion.value = '';
    return;
  }

  const rec = getRecommendedJavaFromParsed(parsed);
  recommendedJavaVersion.value = rec;
  detectedGameVersion.value = parsed[2] > 0
    ? `${parsed[0]}.${parsed[1]}.${parsed[2]}`
    : `${parsed[0]}.${parsed[1]}`;

  // 自动切换在线 Java 选项
  const matchOnline = javaVersions.value.find((v) => v.value === rec);
  if (matchOnline) selectedJavaVersion.value = rec;

  // 自动切换 Docker preset
  const dockerChoices = ['25', '21', '17', '11', '8'];
  if (dockerChoices.includes(rec)) dockerImagePresetVersion.value = rec;

  // 自动切换本机 Java
  const matchLocal = localJavaVersionsRaw.value.find((v) => v.javaVer === rec);
  if (matchLocal) customJavaPath.value = matchLocal.value;
};

watch(
  [onlineGameVersion, () => formData.value.core],
  () => updateJavaSelectionByRecommendation(),
  { deep: true },
);
</script>

<template>
  <div class="mx-auto pb-6 text-[var(--td-text-color-primary)]">
    <div
      class="design-card bg-[var(--td-bg-color-container)]/80 rounded-3xl border border-[var(--td-component-border)] shadow-sm p-6 sm:p-8 transition-all duration-300 flex flex-col md:flex-row gap-8 lg:gap-12 min-h-[600px]"
    >
      <div
        class="w-full md:w-56 shrink-0 md:border-r border-dashed border-zinc-200/80 dark:border-zinc-700/60 md:pr-8 pb-4 md:pb-0 border-b md:border-b-0"
      >
        <t-steps
          layout="vertical"
          :current="currentStep"
          status="process"
          readonly
          class="custom-steps !bg-transparent !mt-2"
        >
          <t-step-item title="基本信息" content="填写实例名称和路径" />
          <t-step-item title="Python 环境" content="配置 MCDR 运行环境" />
          <t-step-item title="核心文件" content="指定内部服务端核心" />
          <t-step-item title="Java 环境" content="配置内部服务端 Java" />
          <t-step-item title="资源配置" content="设置内存与高级选项" />
          <t-step-item title="确认信息" content="核对并提交" />
          <t-step-item title="创建实例" content="提交并等待创建" />
          <t-step-item title="完成" content="查看创建结果" />
        </t-steps>
      </div>

      <div class="flex-1 min-w-0 flex flex-col relative">
        <div v-if="!isCreating && !isSuccess" class="h-full flex flex-col">
          <t-form
            ref="formRef"
            :data="formData"
            :rules="FORM_RULES"
            label-align="top"
            class="flex-1 flex flex-col [&_.t-form__item]:!mb-6"
            @submit="onSubmit"
          >
            <div v-show="currentStep === 0" class="list-item-anim flex-1 pt-1">
              <t-alert theme="info" class="!mb-6 !rounded-xl">
                <template #message>
                  <div class="text-sm leading-relaxed">
                    <b>MCDReforged (MCDR)</b> 是基于 Python 的服务端包装器。此模式会自动搭建
                    <code class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded">MCDR + 真实服务端</code>
                    的目录结构(真实服务端位于 <code class="font-mono">server/</code> 子目录)，并生成 config.yml。
                    需要本机已安装 <b>Python 3.8+</b>。
                  </div>
                </template>
              </t-alert>

              <t-form-item label="实例名称" name="name">
                <t-input
                  v-model="formData.name"
                  placeholder="给你的 MCDR 服务器起个名字"
                  class="!w-full sm:!w-[28rem]"
                />
              </t-form-item>

              <t-form-item
                label="实例路径"
                name="path"
                :help="
                  userStore.userInfo.systemInfo.docker
                    ? '您正在使用Docker容器部署，为保数据安全，仅支持使用默认数据路径'
                    : '选填，留空将使用默认路径'
                "
              >
                <t-input
                  v-model="formData.path"
                  :disabled="userStore.userInfo.systemInfo.docker"
                  placeholder="例如: D:\MyMCDRServer"
                  class="!w-full sm:!w-[28rem] !font-mono"
                />
              </t-form-item>
            </div>

            <div v-show="currentStep === 1" class="list-item-anim flex-1 pt-1">
              <t-alert theme="info" title="Python 环境说明" class="!mb-6 !rounded-xl">
                <template #message>
                  <div class="text-sm leading-relaxed mt-2">
                    MCDR 需要 <b>Python 3.8+</b> 环境运行。建议直接选择已检测到的 Python，若未安装
                    MCDReforged，创建实例时可自动通过 pip 安装。
                  </div>
                </template>
              </t-alert>

              <t-form-item label="Python 来源" name="mcdrPython" class="!mb-0">
                <div class="w-full">
                  <t-radio-group v-model="pythonType" variant="default-filled" class="!mb-4">
                    <t-radio-button value="detected">已检测到的 Python</t-radio-button>
                    <t-radio-button value="custom">自定义命令 / 路径</t-radio-button>
                  </t-radio-group>

                  <div class="w-full sm:w-[32rem] min-h-[70px] mt-2">
                    <div v-if="pythonType === 'detected'" class="flex items-center gap-3">
                      <t-select
                        v-model="detectedPython"
                        :options="
                          pythonList.map((p) => ({
                            label: `${p.path}  ·  Python ${p.version}${
                              p.hasMcdr ? ` · MCDR ${p.mcdrVersion}` : ' · 未装 MCDR'
                            }`,
                            value: p.path,
                          }))
                        "
                        :loading="isScanningPython"
                        placeholder="未检测到 Python，请切换到自定义"
                        class="!flex-1"
                      />
                      <t-button variant="outline" theme="primary" :loading="isScanningPython" @click="fetchPython(true)"
                        >重新扫描</t-button
                      >
                    </div>

                    <div v-if="pythonType === 'custom'">
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
            </div>

            <div v-show="currentStep === 2" class="list-item-anim flex-1 pt-1">
              <t-form-item label="选择内部服务端使用的核心" class="!mb-5">
                <t-radio-group v-model="downloadType" variant="default-filled">
                  <t-radio-button value="online">在线下载 (推荐)</t-radio-button>
                  <t-radio-button value="manual">选择本地文件</t-radio-button>
                  <t-radio-button value="custom">自定义文件名</t-radio-button>
                </t-radio-group>
              </t-form-item>

              <div class="w-full sm:w-[32rem]">
                <div v-if="downloadType === 'online'">
                  <t-form-item label="选择服务端核心" name="coreUrl" class="!mb-0">
                    <div class="w-full">
                      <t-button
                        variant="outline"
                        class="!w-full !justify-start !pl-4 !h-10 !bg-transparent border-zinc-200 dark:border-zinc-700 hover:!border-[var(--color-primary)]"
                        @click="showCoreSelector = true"
                      >
                        <template #icon><t-icon name="cloud-download" class="opacity-70" /></template>
                        点击打开服务端核心选择库
                      </t-button>

                      <div
                        v-if="formData.core"
                        class="flex items-center gap-3 mt-4 p-3 bg-transparent rounded-lg border border-[var(--color-primary)]/40 relative overflow-hidden group"
                      >
                        <div class="absolute left-0 top-0 bottom-0 w-1 bg-[var(--color-primary)] opacity-80"></div>
                        <t-icon name="check-circle-filled" class="text-[var(--color-primary)] text-xl shrink-0 ml-1" />
                        <div class="flex-1 min-w-0">
                          <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                            {{ formData.core }}
                          </div>
                          <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                            MSLX 将在稍后帮您自动下载此文件，并部署到 server/ 子目录...
                          </div>
                        </div>
                        <t-button
                          shape="circle"
                          variant="text"
                          theme="danger"
                          class="shrink-0 hover:!bg-red-500/10 opacity-0 group-hover:opacity-100 transition-opacity"
                          @click="
                            formData.core = '';
                            formData.coreUrl = '';
                          "
                        >
                          <t-icon name="close" />
                        </t-button>
                      </div>
                    </div>
                  </t-form-item>
                  <input v-model="formData.coreSha256" type="hidden" />
                </div>

                <div v-if="downloadType === 'manual'">
                  <t-form-item label="上传核心文件" name="coreFileKey" class="!mb-0">
                    <div class="w-full">
                      <input
                        ref="uploadInputRef"
                        accept=".jar"
                        type="file"
                        style="display: none"
                        @change="onFileChange"
                      />

                      <t-button
                        v-if="!isUploading && !formData.coreFileKey"
                        variant="outline"
                        class="!w-full !justify-start !pl-4 !h-10 !bg-transparent border-zinc-200 dark:border-zinc-700 hover:!border-[var(--color-primary)]"
                        @click="triggerFileSelect"
                      >
                        <template #icon><t-icon name="upload" class="opacity-70" /></template>
                        点击选择文件并上传 (.jar)
                      </t-button>

                      <div
                        v-if="isUploading"
                        class="w-full bg-transparent p-4 mt-4 rounded-lg border border-[var(--color-primary)]/40"
                      >
                        <div class="text-sm font-bold text-[var(--td-text-color-primary)] mb-2 truncate">
                          正在上传: {{ uploadedFileName }} ({{ uploadedFileSize }})
                        </div>
                        <t-progress
                          theme="line"
                          :percentage="uploadProgress"
                          :label="`${Math.round(uploadProgress)}%`"
                        />
                        <div class="text-[11px] text-zinc-500 mt-2 text-center">别着急，喝杯茶🍵...</div>
                      </div>

                      <div
                        v-if="formData.coreFileKey && !isUploading"
                        class="flex items-center gap-3 mt-4 p-3 bg-transparent rounded-lg border border-[var(--color-success)]/40 relative overflow-hidden"
                      >
                        <div class="absolute left-0 top-0 bottom-0 w-1 bg-[var(--color-success)] opacity-80"></div>
                        <t-icon name="check-circle-filled" class="text-[var(--color-success)] text-xl shrink-0 ml-1" />
                        <div class="flex-1 min-w-0">
                          <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                            {{ uploadedFileName }}
                          </div>
                          <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                            {{ uploadedFileSize }} | 已上传准备就绪
                          </div>
                        </div>
                        <div class="flex items-center gap-1 shrink-0">
                          <t-button shape="square" variant="text" theme="primary" @click="triggerFileSelect"
                            ><t-icon name="swap"
                          /></t-button>
                          <t-button shape="square" variant="text" theme="danger" @click="removeUploadedFile"
                            ><t-icon name="delete"
                          /></t-button>
                        </div>
                      </div>
                    </div>
                  </t-form-item>
                </div>

                <div v-if="downloadType === 'custom'">
                  <t-alert theme="warning" class="!mb-5 !rounded-xl">
                    <template #message
                      >此模式通常用于服务器目录中已经存在核心文件，或者您打算稍后手动通过文件管理上传核心到 server/
                      子目录。</template
                    >
                  </t-alert>
                  <t-form-item label="核心文件名" name="core" class="!mb-0">
                    <template #help
                      ><span class="text-[11px] text-zinc-500 mt-1 inline-block"
                        >请确保文件名包含后缀，例如: server.jar</span
                      ></template
                    >
                    <t-input v-model="formData.core" placeholder="请输入核心文件名" class="!font-mono" />
                  </t-form-item>
                </div>
              </div>
            </div>

            <div v-show="currentStep === 3" class="list-item-anim flex-1 pt-1">
              <t-alert v-if="recommendedJavaVersion" theme="success" :title="`推荐 Java 版本: Java ${recommendedJavaVersion}`" class="!mb-6 !rounded-xl">
                <template #message>
                  检测到您的游戏版本为 <span class="font-bold text-[var(--color-success)]">{{ detectedGameVersion }}</span>，已为您自动选择推荐的 Java {{ recommendedJavaVersion }}。此 Java 用于运行 MCDR 内部的真实服务端核心。
                </template>
              </t-alert>

              <t-alert v-else theme="info" title="Java 版本选择指南" class="!mb-6 !rounded-xl">
                <template #message>
                  <div class="flex flex-col gap-2.5 mt-2">
                    <div class="flex items-center gap-3">
                      <span
                        class="inline-flex items-center justify-center w-[140px] px-2 py-1 rounded bg-[var(--color-primary)] text-white font-bold text-xs tracking-wide shadow-sm"
                        >MC 26.1 - 最新版本</span
                      >
                      <span
                        class="font-extrabold text-xs text-[var(--color-success)] bg-[var(--color-success)]/10 px-2.5 py-1 rounded-md border border-[var(--color-success)]/20"
                        >Java 25</span
                      >
                    </div>
                    <div class="flex items-center gap-3">
                      <span
                        class="inline-flex items-center justify-center w-[140px] px-2 py-1 rounded bg-[var(--color-primary)] text-white font-bold text-xs tracking-wide shadow-sm"
                        >MC 1.20.5 - 1.21.11</span
                      >
                      <span
                        class="font-extrabold text-xs text-[var(--color-success)] bg-[var(--color-success)]/10 px-2.5 py-1 rounded-md border border-[var(--color-success)]/20"
                        >Java 21</span
                      >
                    </div>
                    <div class="flex items-center gap-3">
                      <span
                        class="inline-flex items-center justify-center w-[140px] px-2 py-1 rounded bg-[var(--color-primary)] text-white font-bold text-xs tracking-wide shadow-sm"
                        >MC 1.18 - 1.20.4</span
                      >
                      <span
                        class="font-extrabold text-xs text-[var(--color-success)] bg-[var(--color-success)]/10 px-2.5 py-1 rounded-md border border-[var(--color-success)]/20"
                        >Java 17</span
                      >
                    </div>
                    <div class="flex items-center gap-3">
                      <span
                        class="inline-flex items-center justify-center w-[140px] px-2 py-1 rounded bg-[var(--color-primary)] text-white font-bold text-xs tracking-wide shadow-sm"
                        >MC 1.17 / 1.17.1</span
                      >
                      <span
                        class="font-extrabold text-xs text-[var(--color-success)] bg-[var(--color-success)]/10 px-2.5 py-1 rounded-md border border-[var(--color-success)]/20"
                        >Java 16</span
                      >
                    </div>
                    <div class="flex items-center gap-3">
                      <span
                        class="inline-flex items-center justify-center w-[140px] px-2 py-1 rounded bg-zinc-100 dark:bg-zinc-800 text-zinc-600 dark:text-zinc-300 border border-zinc-200 dark:border-zinc-700 font-bold text-xs tracking-wide shadow-sm"
                        >MC 1.13 - 更低版本</span
                      >
                      <span
                        class="font-extrabold text-xs text-[var(--td-text-color-secondary)] bg-zinc-100 dark:bg-zinc-800 px-2.5 py-1 rounded-md border border-zinc-200 dark:border-zinc-700"
                        >Java 8</span
                      >
                    </div>
                    <div
                      class="text-[11px] text-[var(--td-text-color-secondary)] mt-1 flex items-center gap-1 font-medium"
                    >
                      <t-icon name="info-circle" size="14px" /> 此 Java 用于运行 MCDR 内部的真实服务端核心。
                    </div>
                  </div>
                </template>
              </t-alert>

              <t-form-item label="内部服务端 Java 来源" name="java" class="!mb-0">
                <div class="w-full">
                  <t-radio-group v-model="javaType" variant="default-filled" class="!mb-4">
                    <t-radio-button value="online">在线下载</t-radio-button>
                    <t-radio-button value="local">选择电脑上的 Java</t-radio-button>
                    <t-radio-button value="env">环境变量</t-radio-button>
                    <t-radio-button value="custom">自定义路径</t-radio-button>
                  </t-radio-group>

                  <div class="w-full sm:w-[32rem] min-h-[70px] mt-2">
                    <div v-if="javaType === 'online'">
                      <t-select
                        v-model="selectedJavaVersion"
                        :options="javaVersionsWithOptions"
                        placeholder="请选择 Java 版本"
                        class="!w-full sm:!w-64"
                      />
                      <div class="text-[11px] text-[var(--td-text-color-secondary)] mt-2 font-medium">
                        将下载并使用 Java {{ selectedJavaVersion || '?' }}
                        <span class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded ml-1"
                          >{{ userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', '') }} /
                          {{ userStore.userInfo.systemInfo.osArchitecture.toLowerCase() }}</span
                        >
                      </div>
                    </div>

                    <div v-if="javaType === 'local'" class="flex items-center gap-3">
                      <t-select
                        v-model="customJavaPath"
                        :options="localJavaVersionsWithOptions"
                        placeholder="请选择 Java 版本"
                        class="!flex-1"
                      />
                      <t-button variant="outline" theme="primary" @click="fetchJavaVersions(true)">重新扫描</t-button>
                    </div>

                    <div v-if="javaType === 'env'">
                      <t-input
                        model-value="java"
                        readonly
                        disabled
                        class="!font-mono !bg-zinc-100 dark:!bg-zinc-800/50"
                      />
                      <div class="text-[11px] text-zinc-500 mt-2 font-medium">将使用系统环境变量中的 java 命令</div>
                    </div>

                    <div v-if="javaType === 'custom'">
                      <t-input
                        v-model="customJavaPath"
                        placeholder="例如: C:\Program Files\Java\jdk-17\bin\java.exe"
                        class="!font-mono"
                      />
                    </div>
                  </div>
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 4" class="list-item-anim flex-1 pt-1">
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-6 w-full sm:w-[40rem]">
                <t-form-item label="最小内存" name="minM" class="!mb-0">
                  <div class="flex items-center gap-2 w-full">
                    <div class="flex-1">
                      <t-input-number
                        v-model="minMComputed"
                        :min="0"
                        :decimal-places="minUnit === 'GB' ? 1 : 0"
                        placeholder="Xms"
                        theme="column"
                        class="!w-full"
                      />
                    </div>
                    <t-select v-model="minUnit" :options="unitOptions" :clearable="false" class="!w-20 shrink-0" />
                  </div>
                </t-form-item>

                <t-form-item label="最大内存" name="maxM" class="!mb-0">
                  <div class="flex items-center gap-2 w-full">
                    <div class="flex-1">
                      <t-input-number
                        v-model="maxMComputed"
                        :min="0"
                        :decimal-places="maxUnit === 'GB' ? 1 : 0"
                        placeholder="Xmx"
                        theme="column"
                        class="!w-full"
                      />
                    </div>
                    <t-select v-model="maxUnit" :options="unitOptions" :clearable="false" class="!w-20 shrink-0" />
                  </div>
                </t-form-item>
              </div>

              <t-form-item label="额外 JVM 参数 (可选)" name="args" class="!mt-8 w-full sm:w-[40rem]">
                <template #help
                  ><span class="text-[11px] text-zinc-500 mt-1 inline-block">例如: -XX:+UseG1GC</span></template
                >
                <t-textarea
                  v-model="formData.args"
                  placeholder="-XX:+UseG1GC"
                  :autosize="{ minRows: 3, maxRows: 6 }"
                  class="!font-mono !bg-transparent"
                />
              </t-form-item>

              <t-collapse :default-value="[]" class="!mt-8 !bg-transparent !border-none">
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

                  <t-form-item label="自动安装 MCDReforged" class="!mb-6">
                    <div class="flex items-center gap-3">
                      <t-switch v-model="formData.mcdrInstall" size="large" />
                      <span class="text-sm">{{
                        formData.mcdrInstall ? '创建时自动 pip 安装' : '不自动安装(需自行安装)'
                      }}</span>
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
            </div>

            <div v-show="currentStep === 5" class="list-item-anim flex-1 pt-1">
              <div class="flex flex-col min-w-0 mb-8 pb-6 border-b border-zinc-200 dark:border-zinc-800">
                <div class="text-xl font-extrabold text-[var(--td-text-color-primary)] truncate tracking-tight">
                  {{ formData.name }}
                </div>
                <div class="text-sm text-[var(--td-text-color-secondary)] mt-2 flex items-center gap-1.5 truncate">
                  <t-icon name="folder-open" class="opacity-70" />
                  {{ formData.path || '默认数据路径 (/DaemonData/Servers)' }}
                </div>
              </div>

              <div class="flex flex-col w-full">
                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >Python 环境</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        :title="formData.mcdrPython"
                        >{{ formData.mcdrPython }}</span
                      >
                      <t-tag
                        v-if="pythonType === 'detected'"
                        theme="success"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >已检测</t-tag
                      >
                      <t-tag v-else theme="default" variant="light" size="small" class="!rounded">自定义</t-tag>
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-1">
                      <span v-if="formData.mcdrInstall">创建时将自动 pip 安装 MCDReforged</span>
                      <span v-else>不自动安装 MCDReforged</span>
                    </div>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >服务端核心</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        >{{ formData.core || '未指定' }}</span
                      >
                      <t-tag
                        v-if="downloadType === 'online'"
                        theme="primary"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >在线下载</t-tag
                      >
                      <t-tag
                        v-else-if="downloadType === 'manual'"
                        theme="warning"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >手动上传</t-tag
                      >
                      <t-tag v-else theme="default" variant="light" size="small" class="!rounded">自定义</t-tag>
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-1">
                      <span v-if="downloadType === 'online'"
                        >来源: MSL 镜像源 ({{ formData.coreUrl ? '已匹配' : '未匹配' }})</span
                      >
                      <span v-else>大小: {{ uploadedFileSize || '未知' }}</span>
                    </div>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >Java 运行时</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span v-if="javaType === 'online'" class="text-sm font-bold text-[var(--td-text-color-primary)]"
                        >Java {{ selectedJavaVersion }}</span
                      >
                      <span
                        v-else
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        :title="formData.java"
                        >{{ formData.java }}</span
                      >
                      <t-tag v-if="javaType === 'online'" theme="success" variant="light" size="small" class="!rounded"
                        >自动安装</t-tag
                      >
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-1 truncate max-w-[250px] sm:max-w-[350px]">
                      <span v-if="javaType === 'online'">将自动从镜像源下载并解压 JDK</span>
                      <span v-else>目标环境: {{ formData.java }}</span>
                    </div>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >内存分配 (JVM)</span
                  >
                  <div class="flex items-center gap-3">
                    <span class="text-sm font-bold text-[var(--color-primary)]"
                      >初始 (Xms): {{ minMComputed }} {{ minUnit }}</span
                    >
                    <t-icon name="arrow-right" class="text-zinc-300 dark:text-zinc-600" />
                    <span class="text-sm font-bold text-red-500 dark:text-red-400"
                      >最大 (Xmx): {{ maxMComputed }} {{ maxUnit }}</span
                    >
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >MCDR Handler</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <span class="text-sm font-bold text-[var(--td-text-color-primary)]">
                      {{ handlerType === 'auto' ? '自动推断' : formData.mcdrHandler || '未指定' }}
                    </span>
                    <div class="text-[11px] text-zinc-500 mt-1">
                      {{ handlerType === 'auto' ? '根据核心文件名自动匹配' : '已手动指定' }}
                    </div>
                  </div>
                </div>

                <div class="flex flex-col sm:flex-row sm:items-start justify-between py-4">
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-2 sm:mb-0 shrink-0 mt-1"
                    >启动参数</span
                  >
                  <div
                    v-if="formData.args"
                    class="text-xs font-mono text-[var(--td-text-color-secondary)] break-all leading-relaxed bg-zinc-50/50 dark:bg-zinc-800/30 p-2.5 rounded-lg border border-zinc-100 dark:border-zinc-800 text-left sm:text-right max-w-full sm:max-w-md"
                  >
                    {{ formData.args }}
                  </div>
                  <div v-else class="text-sm text-zinc-500 mt-1">无额外参数</div>
                </div>
              </div>

              <t-alert
                theme="info"
                class="!mt-8 !rounded-xl !bg-[var(--color-primary)]/5 !border-[var(--color-primary)]/20"
              >
                <template #message
                  >确认无误后点击下方
                  <strong class="text-[var(--color-primary)] mx-1">提交创建</strong>，系统将自动开始搭建 MCDR
                  目录结构、下载资源并部署实例。</template
                >
              </t-alert>
            </div>

            <div class="mt-auto pt-6 border-t border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
              <t-button v-if="currentStep > 0 && currentStep < 6" theme="default" @click="prevStep">上一步</t-button>
              <div v-else></div>
              <t-button v-if="currentStep < 5" theme="primary" type="button" @click="nextStep">下一步</t-button>
              <t-button v-if="currentStep === 5" theme="primary" type="submit" :loading="isSubmitting"
                >提交创建</t-button
              >
            </div>
          </t-form>
        </div>

        <div v-if="isCreating" class="h-full flex flex-col items-center justify-center py-8 list-item-anim">
          <div class="text-lg font-bold text-[var(--td-text-color-primary)] mb-2 tracking-tight">
            正在创建 MCDR 实例 ({{ createdServerId }})
          </div>
          <p class="text-sm text-[var(--td-text-color-secondary)] mb-6">
            请勿关闭此页面，安装 MCDR 与下载核心可能需要几分钟...
          </p>

          <div class="w-full max-w-lg !my-6">
            <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(0)}%`" />
          </div>

          <div
            class="w-full max-w-2xl bg-white/40 dark:bg-zinc-900/40 rounded-2xl border border-white/60 dark:border-zinc-700/50 p-4 h-64 flex flex-col mt-6 shadow-[0_4px_12px_rgba(0,0,0,0.02)]"
          >
            <div ref="logContainerRef" class="flex-1 overflow-y-auto custom-scrollbar pr-2">
              <div v-for="(log, index) in statusMessages" :key="index" class="text-xs font-mono mb-2 leading-relaxed">
                <span class="text-[var(--td-text-color-secondary)] mr-2">[{{ log.time }}]</span>
                <span class="text-[var(--td-text-color-primary)] font-medium">{{ log.message }}</span>
              </div>
            </div>
          </div>
        </div>

        <div
          v-if="isSuccess"
          class="h-full flex flex-col items-center justify-center py-8 list-item-anim min-h-[50vh] sm:min-h-[40vh]"
        >
          <t-icon name="check-circle" size="64px" class="text-[var(--color-success)]" />

          <div class="text-xl text-[var(--td-text-color-primary)] text-center font-medium leading-[22px] !mt-4">
            MCDR 服务器 ({{ createdServerId }}) 已创建成功
          </div>

          <div class="text-sm text-[var(--td-text-color-secondary)] leading-[22px] !my-2 !mb-8">
            首次启动时如提示 EULA，请在面板同意后继续
          </div>

          <div class="flex gap-4">
            <t-button
              @click="
                () => {
                  goToHome();
                  changeUrl('/instance/list');
                }
              "
              >返回服务端列表</t-button
            >
            <t-button
              theme="default"
              @click="
                () => {
                  goToHome();
                  changeUrl(`/instance/console/${createdServerId}`);
                }
              "
              >前往控制台</t-button
            >
          </div>
        </div>
      </div>
    </div>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />
  </div>
</template>

<style scoped lang="less">
@import '@/style/scrollbar';
@reference "@/style/tailwind/index.css";

/* 进场动画 */
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

.custom-scrollbar {
  .scrollbar-mixin();
}

:deep(.custom-steps) {
  .t-steps-item__inner {
    @apply !flex !items-start !gap-4 !pb-8 !m-0;
  }

  .t-steps-item__icon {
    @apply !w-8 !h-8 !rounded-full !flex !items-center !justify-center !border-2 !text-sm !font-extrabold !bg-transparent !transition-colors !duration-300 !z-10 !relative;
  }
  .t-steps-item--process .t-steps-item__icon {
    @apply !border-[var(--color-primary)] !text-[var(--color-primary)] !bg-[var(--color-primary)]/10 shadow-[0_0_12px_var(--color-primary-light)]/40;
  }
  .t-steps-item--default .t-steps-item__icon {
    @apply !border-zinc-200 dark:!border-zinc-700 !text-zinc-400 dark:!text-zinc-500 !bg-transparent;
  }
  .t-steps-item--finish .t-steps-item__icon {
    @apply !border-[var(--color-success)] !text-[var(--color-success)] !bg-[var(--color-success)]/10;
  }

  .t-steps-item__title {
    @apply !text-sm !font-extrabold !text-zinc-800 dark:!text-zinc-200 !leading-none !mb-1.5 !transition-colors;
  }
  .t-steps-item--process .t-steps-item__title {
    @apply !text-[var(--color-primary)];
  }
  .t-steps-item__description {
    @apply !text-xs !font-medium !text-zinc-500 dark:!text-zinc-400 !leading-relaxed;
  }

  .t-steps-item:not(:last-child)::after {
    content: '';
    @apply !absolute !w-[2px] !bg-zinc-200 dark:!bg-zinc-700 !top-8 !bottom-0 !left-[15px] !z-0;
  }
  .t-steps-item--finish:not(:last-child)::after {
    @apply !bg-[var(--color-primary)]/50;
  }
}
</style>
