<script setup lang="ts">
import { onUnmounted, ref, watch, onMounted, computed, nextTick } from 'vue';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';
import { FolderOpenIcon } from 'tdesign-icons-vue-next';

import ServerCoreSelector from './ServerCoreSelector.vue';
import HostFileSelector from './HostFileSelector.vue';
import { getJavaVersionList } from '@/api/mslapi/java';
import { getLocalJavaList } from '@/api/localJava';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { deleteUpload, checkPackageJarList } from '@/api/files';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { useInstanceListStore } from '@/store/modules/instance';
import { useFileUpload } from '@/hooks/useFileUpload';

const { isUploading, uploadProgress, uploadedFileName, uploadedFileSize, startUpload, removeUploadData } =
  useFileUpload();

// 状态管理
const userStore = useUserStore();
const formRef = ref(null);

const instanceListStore = useInstanceListStore();

const currentStep = ref(0);
const isSubmitting = ref(false);

const showHostFileSelector = ref(false);

const isCreating = ref(false);
const isSuccess = ref(false);
const progress = ref(0); // SignalR 创建进度
const statusMessages = ref<{ time: string; message: string; progress: number | null }[]>([]);
const hubConnection = ref<HubConnection | null>(null);
const createdServerId = ref<string | null>(null);
const logContainerRef = ref<HTMLDivElement | null>(null); // 日志容器dom

// 整合包导入方式
const packageSourceType = ref('upload'); // 'upload' | 'localPath'

// 核心选择相关状态
const downloadType = ref('online'); // 'online' | 'manual' (仅在未检测到Jar时使用)
const showCoreSelector = ref(false);

// 整合包检测相关状态
const detectedJars = ref<string[]>([]);
const isCheckingPackage = ref(false);
const detectedRoot = ref('');

// Input DOM 引用
const uploadInputRef = ref<HTMLInputElement | null>(null);

// Java 选择相关状态
const javaType = ref('online');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersionsRaw = ref<any[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');
const onlineGameVersion = ref('');

// 版本号解析与推荐
const parseMcVersion = (filename: string): string | null => {
  if (!filename) return null;
  const mc1xRegex = /\b(1\.\d+(?:\.\d+)*)\b/;
  const match1x = filename.match(mc1xRegex);
  if (match1x) {
    return match1x[1];
  }
  const generalRegex = /\b(\d+\.\d+(?:\.\d+)*)\b/;
  const matchGeneral = filename.match(generalRegex);
  if (matchGeneral) {
    return matchGeneral[1];
  }
  return null;
};

const compareVersions = (v1: string, v2: string): number => {
  const parts1 = v1.split('.').map(Number);
  const parts2 = v2.split('.').map(Number);
  const maxLen = Math.max(parts1.length, parts2.length);
  for (let i = 0; i < maxLen; i++) {
    const p1 = parts1[i] || 0;
    const p2 = parts2[i] || 0;
    if (p1 !== p2) {
      return p1 - p2;
    }
  }
  return 0;
};

const getRecommendedJava = (version: string): number | null => {
  if (!version) return null;
  if (compareVersions(version, '26.1') >= 0) {
    return 25;
  }
  if (compareVersions(version, '1.20.5') >= 0) {
    return 21;
  }
  if (compareVersions(version, '1.18') >= 0) {
    return 17;
  }
  if (compareVersions(version, '1.17') >= 0) {
    return 16;
  }
  return 8;
};

const detectedGameVersion = computed(() => {
  if (detectedJars.value.length === 0 && downloadType.value === 'online') {
    return onlineGameVersion.value;
  }
  return parseMcVersion(formData.value.core);
});

const recommendedJavaVersion = computed(() => {
  const version = detectedGameVersion.value;
  if (!version) return null;
  const rec = getRecommendedJava(version);
  return rec ? String(rec) : null;
});

const javaVersionsWithOptions = computed(() => {
  return javaVersions.value.map((v) => {
    const isRec = recommendedJavaVersion.value && v.value === recommendedJavaVersion.value;
    return {
      ...v,
      label: isRec ? `${v.label} (推荐)` : v.label
    };
  });
});

const localJavaVersionsWithOptions = computed(() => {
  return localJavaVersions.value.map((v) => {
    if (recommendedJavaVersion.value) {
      const raw = localJavaVersionsRaw.value.find(item => item.path === v.value);
      if (raw) {
        const vStr = String(raw.version);
        const isRec = vStr === recommendedJavaVersion.value || vStr.startsWith(recommendedJavaVersion.value + '.');
        if (isRec) {
          return {
            ...v,
            label: `${v.label} (推荐)`
          };
        }
      }
    }
    return v;
  });
});

// 本地和在线java
const fetchJavaVersions = async (force: boolean = false) => {
  try {
    if (force) MessagePlugin.info('正在刷新Java版本列表...');
    const res = await getJavaVersionList(
      userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', ''),
      userStore.userInfo.systemInfo.osArchitecture.toLowerCase(),
    );
    if (res && Array.isArray(res)) {
      javaVersions.value = res.map((v) => ({ label: `Java ${v}`, value: v }));
    }
    const rawLocal = await getLocalJavaList(force);
    localJavaVersionsRaw.value = rawLocal;
    localJavaVersions.value = rawLocal.map((v) => ({
      label: `Java ${v.version}${v.is64Bit ? '' : ' (32位)'} (${v.vendor} | ${v.path})`,
      value: v.path,
    }));
    if (force) MessagePlugin.success('已刷新Java版本列表');
  } catch (e: any) {
    MessagePlugin.warning('获取在线Java版本失败: ' + e.message);
  }
};

onMounted(() => {
  fetchJavaVersions();
});

// 表单
const formData = ref(<CreateInstanceQucikModeModel>{
  name: '新建整合包服务器',
  path: '',
  java: '',
  core: '', // 最终确定的启动核心文件名
  coreUrl: '', // 仅当未检测到Jar且选择在线下载时使用
  coreSha256: '', // 同上
  coreFileKey: '', // 这里不给手动上传核心
  packageFileKey: '', // 整合包 Zip 文件的 Key
  packageLocalPath: '', // 整合包本地绝对路径
  minM: 2048,
  maxM: 6144,
  args: '',
  dockerImage: 'MSLX://DockerImage/Java/25',
  dockerPorts: '25565:25565',
});

// 处理内存单位
const minUnit = ref('GB');
const maxUnit = ref('GB');
const unitOptions = [
  { label: 'MB', value: 'MB' },
  { label: 'GB', value: 'GB' },
];

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

// 监听选择java的状态变量 修改表单数据
watch(
  [javaType, selectedJavaVersion, customJavaPath, () => formData.value.dockerImage],
  ([type, ver, path, dockerImg]) => {
    if (type === 'env') formData.value.java = 'java';
    else if (type === 'custom' || type === 'local') formData.value.java = path;
    else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';
    else if (type === 'docker') {
      formData.value.java = dockerImg.startsWith('MSLX://') ? 'docker-java' : 'docker-custom';
    }

    if (formData.value.java) {
      formRef.value?.validate({ fields: ['java'] });
    }
  },
  { immediate: true },
);

// 处理选择本地整合包zip
const onHostFileSelected = async (absolutePath: string) => {
  formData.value.packageLocalPath = absolutePath;
  await analyzePackage('', absolutePath);
};

// docker
const dockerImageType = ref('preset');
const dockerImagePresetVersion = ref('25');

watch([dockerImageType, dockerImagePresetVersion], ([type, ver]) => {
  if (javaType.value === 'docker') {
    if (type === 'preset') {
      formData.value.dockerImage = `MSLX://DockerImage/Java/${ver}`;
    }
  }
});

const isDockerMode = computed(() => javaType.value === 'docker');

// 动态多端口放行管理器
const localPortList = ref<{ host: number; container: number; protocol: 'tcp' | 'udp' }[]>([
  { host: 25565, container: 25565, protocol: 'tcp' },
]);

const networkMode = ref<'mapped' | 'host'>('mapped');

watch(
  [localPortList, networkMode],
  ([list, mode]) => {
    if (mode === 'host') {
      formData.value.dockerPorts = '0';
    } else {
      if (list.length === 0) {
        formData.value.dockerPorts = '';
      } else {
        formData.value.dockerPorts = list
          .filter((p) => p.host > 0 && p.container > 0)
          .map((p) => `${p.host}:${p.container}/${p.protocol}`)
          .join(',');
      }
    }
  },
  { deep: true, immediate: true },
);

const addPortMapping = () => {
  if (networkMode.value === 'host') {
    networkMode.value = 'mapped';
  }
  localPortList.value.push({ host: 25565, container: 25565, protocol: 'tcp' });
};

const removePortMapping = (index: number) => {
  localPortList.value.splice(index, 1);
};

const updateJavaSelectionByRecommendation = () => {
  const targetVer = recommendedJavaVersion.value || '25';

  // 在线版本
  if (javaVersions.value.length > 0) {
    const hasOnline = javaVersions.value.some((v) => v.value === targetVer);
    if (hasOnline) {
      selectedJavaVersion.value = targetVer;
    } else {
      selectedJavaVersion.value = javaVersions.value[0].value;
    }
  } else {
    selectedJavaVersion.value = targetVer;
  }

  // Docker
  dockerImagePresetVersion.value = targetVer;

  // 本地 Java
  if (localJavaVersionsRaw.value && localJavaVersionsRaw.value.length > 0) {
    const found = localJavaVersionsRaw.value.find(v => {
      const vStr = String(v.version);
      return vStr === targetVer || vStr.startsWith(targetVer + '.');
    });
    if (found) {
      customJavaPath.value = found.path;
    } else {
      customJavaPath.value = localJavaVersionsRaw.value[0].path;
    }
  }
};

watch([recommendedJavaVersion, javaVersions, localJavaVersionsRaw], () => {
  updateJavaSelectionByRecommendation();
}, { deep: true, immediate: true });

// 表单和步骤校验
const FORM_RULES = computed<FormRules>(() => {
  return {
    name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
    packageFileKey: [
      {
        validator: (val) => packageSourceType.value === 'localPath' || !!val,
        message: '请上传整合包文件',
        type: 'error',
        trigger: 'change',
      },
    ],
    packageLocalPath: [
      {
        validator: (val) => packageSourceType.value === 'upload' || !!val,
        message: '请输入本机绝对路径',
        type: 'error',
        trigger: 'blur',
      },
    ],
    core: [
      {
        validator: (val) => {
          if (detectedJars.value.length > 0) {
            if (!val) return { result: false, message: '请选择一个启动Jar', type: 'error' };
          } else {
            if (downloadType.value === 'online' && !formData.value.coreUrl) {
              return { result: false, message: '请选择服务端核心', type: 'error' };
            }
            if (downloadType.value === 'manual' && !formData.value.coreFileKey) {
              return { result: false, message: '请上传核心文件', type: 'error' };
            }
          }
          return true;
        },
        trigger: 'change',
      },
    ],
    java: [{ required: true, message: '请配置 Java 环境', trigger: 'change' }],
    minM: [{ required: true, min: 1, message: '必须大于0', trigger: 'blur' }],
    maxM: [{ required: true, min: 1, message: '必须大于0', trigger: 'blur' }],
    dockerPorts: [
      {
        validator: (val) => {
          if (javaType.value === 'docker' && val && val !== '0') {
            if (!/^(0|^([0-9]+:[0-9]+)(,[0-9]+:[0-9]+)*)$/.test(val)) {
              return { result: false, message: "端口格式错误，例 '25565:25565' 或用逗号隔开", type: 'error' };
            }
          }
          return true;
        },
        trigger: 'change',
      },
    ],
  };
});

const stepValidationFields = [
  ['name', 'path'],
  ['packageFileKey', 'packageLocalPath'],
  ['core', 'coreUrl', 'coreFileKey'],
  ['java'],
  ['minM', 'maxM', 'args'],
  [],
];

const prevStep = () => {
  if (currentStep.value > 0) currentStep.value -= 1;
};

const nextStep = async () => {
  if (currentStep.value === 1) {
    if (packageSourceType.value === 'upload') {
      if (!formData.value.packageFileKey) {
        MessagePlugin.warning('请先上传服务端整合包(Zip)');
        return;
      }
      if (isUploading.value || isCheckingPackage.value) {
        MessagePlugin.warning('请等待上传或分析完成');
        return;
      }
    } else {
      if (!formData.value.packageLocalPath) {
        MessagePlugin.warning('请输入服务端整合包的绝对路径');
        return;
      }
      if (isCheckingPackage.value) {
        MessagePlugin.warning('正在分析中，请稍候');
        return;
      }
      await analyzePackage('', formData.value.packageLocalPath);
    }
  }

  if (currentStep.value === 2) {
    if (detectedJars.value.length > 0) {
      if (!formData.value.core) {
        MessagePlugin.warning('请从列表中选择一个启动核心');
        return;
      }
    } else {
      if (downloadType.value === 'online' && (!formData.value.coreUrl || !formData.value.core)) {
        MessagePlugin.warning('请选择一个服务端核心');
        return;
      }
      if (downloadType.value === 'manual' && !formData.value.coreFileKey) {
        MessagePlugin.warning('请上传核心文件');
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
  const hasError = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasError) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    if (currentStep.value < 5) currentStep.value += 1;
  }
};

const triggerFileSelect = () => {
  uploadInputRef.value?.click();
};

// 文件选择
const onFileChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;

  // 清理旧状态的云端文件
  if (formData.value.packageFileKey) {
    try {
      await deleteUpload(formData.value.packageFileKey);
    } catch (e) {
      console.error(e);
    }
  }

  // 重置核心相关的旧状态
  formData.value.core = '';
  detectedJars.value = [];
  detectedRoot.value = '';

  const file = input.files[0];

  try {
    const finalKey = await startUpload(file);

    formData.value.packageFileKey = finalKey;
    MessagePlugin.success('上传成功，正在分析整合包内容...');

    // 立即调用分析接口
    await analyzePackage(finalKey);
  } catch (error: any) {
    if (error.message !== '已取消') {
      console.error(error);
      MessagePlugin.error(`上传失败: ${error.message || '未知错误'}`);
      formData.value.packageFileKey = '';
    }
  } finally {
    input.value = '';
  }
};

const analyzePackage = async (key: string, localPath?: string) => {
  isCheckingPackage.value = true;
  try {
    const res = await checkPackageJarList(key, localPath);

    detectedJars.value = res.jars || [];
    detectedRoot.value = res.detectedRoot || '';

    if (res.count === 1 && res.jars.length > 0) {
      formData.value.core = res.jars[0];
      MessagePlugin.success(`自动识别到服务端核心: ${res.jars[0]}`);
    } else if (res.count > 1) {
      MessagePlugin.info(`检测到 ${res.count} 个服务端核心，请在下一步选择`);
    } else {
      MessagePlugin.warning('未检测到服务端核心，请在下一步手动配置');
    }
  } catch (error: any) {
    MessagePlugin.error('整合包分析失败: ' + error.message);
  } finally {
    isCheckingPackage.value = false;
  }
};

// 移除文件
const removeUploadedFile = async () => {
  if (formData.value.packageFileKey) {
    await removeUploadData();
    formData.value.packageFileKey = '';
    detectedJars.value = [];
    formData.value.core = '';
    MessagePlugin.success('文件已移除');
  }
};

const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = '';
  onlineGameVersion.value = data.version;
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);
};

const onSubmit = async () => {
  const validateResult = await formRef.value.validate();
  if (validateResult !== true) {
    MessagePlugin.warning('请检查表单所有内容');
    return;
  }

  if (javaType.value === 'docker') {
    formData.value.java = formData.value.dockerImage.startsWith('MSLX://') ? 'docker-java' : 'docker-custom';
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
  };

  if (packageSourceType.value === 'localPath') {
    apiData.packageFileKey = null;
  } else {
    apiData.packageLocalPath = null;
  }
  if (detectedJars.value.length > 0) {
    apiData.coreUrl = null;
    apiData.coreSha256 = null;
    apiData.coreFileKey = null;
  } else {
    if (downloadType.value === 'manual') {
      apiData.coreUrl = null;
      apiData.coreSha256 = null;
    } else {
      apiData.coreFileKey = null;
    }
  }

  try {
    const response = await postCreateInstanceQuickMode(apiData);
    const serverId = response.serverId;
    if (!serverId) throw new Error('服务器未返回 ServerId');

    createdServerId.value = serverId.toString();
    isCreating.value = true;
    currentStep.value = 6;

    await startSignalRConnection(createdServerId.value);
  } catch (error: any) {
    MessagePlugin.error(error.message || '创建请求失败');
    isSubmitting.value = false;
  }
};

const startSignalRConnection = async (serverId: string) => {
  const { baseUrl, token } = userStore;

  const hubUrl = new URL('/api/hubs/creationProgressHub', baseUrl || window.location.origin);
  hubUrl.searchParams.append('x-user-token', token);

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Information)
    .build();

  hubConnection.value.on('StatusUpdate', (id, message, prog) => {
    if (id.toString() !== serverId) return;
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
    if (prog !== null && prog >= 0) progress.value = prog;

    if (prog === 100) {
      MessagePlugin.success('服务器创建成功！');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSuccess.value = true;
      currentStep.value = 7;
      isSubmitting.value = false;
      instanceListStore.refreshInstanceList();
    } else if (prog === -1) {
      MessagePlugin.error(message || '错误');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSubmitting.value = false;
      currentStep.value = 0;
    }
  });

  try {
    await hubConnection.value.start();
    await hubConnection.value.invoke('TrackServer', serverId);
  } catch (err) {
    console.error('SignalR Error', err);
  }
};

onUnmounted(() => {
  hubConnection.value?.stop();
});

const goToHome = () => {
  isSuccess.value = false;
  currentStep.value = 0;
  formData.value = {
    name: '新建整合包服务器',
    path: '',
    java: '',
    core: '',
    coreUrl: '',
    coreSha256: '',
    coreFileKey: '',
    packageFileKey: '',
    packageLocalPath: '',
    minM: 2048,
    maxM: 6144,
    args: '',
  };
  detectedJars.value = [];
  downloadType.value = 'online';
  javaType.value = 'online';
  onlineGameVersion.value = '';

  removeUploadData();
};
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
          <t-step-item title="基本信息" content="填写实例名称" />
          <t-step-item title="上传整合包" content="上传服务端 Zip 包" />
          <t-step-item title="核心配置" content="确认启动的服务端核心" />
          <t-step-item title="Java 环境" content="配置 Java 运行时" />
          <t-step-item title="资源配置" content="设置内存参数" />
          <t-step-item title="确认信息" content="核对并提交" />
          <t-step-item title="创建实例" content="提交并等待解压" />
          <t-step-item title="完成" content="查看结果" />
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
              <t-form-item label="实例名称" name="name">
                <t-input v-model="formData.name" placeholder="为你的服务器起个名字" class="!w-full sm:!w-[28rem]" />
              </t-form-item>
              <t-form-item
                label="实例路径"
                name="path"
                :help="
                  userStore.userInfo.systemInfo.docker
                    ? '您的MSLX正在运行于Docker环境内，为保数据安全，仅支持使用默认数据路径'
                    : '选填，留空将使用默认路径'
                "
              >
                <t-input
                  v-model="formData.path"
                  :disabled="userStore.userInfo.systemInfo.docker"
                  placeholder="例如: D:\MyServer"
                  class="!w-full sm:!w-[28rem] !font-mono"
                />
              </t-form-item>
            </div>

            <div v-show="currentStep === 1" class="list-item-anim flex-1 pt-1">
              <t-alert theme="info" class="!mb-6 !rounded-xl">
                <template #message
                  >请上传包含服务端文件的 <b>.zip</b> 压缩包，或直接填写守护进程本机上的绝对路径。</template
                >
              </t-alert>

              <t-form-item label="整合包来源" class="!mb-4">
                <t-radio-group v-model="packageSourceType" variant="default-filled">
                  <t-radio-button value="upload">本地上传</t-radio-button>
                  <t-radio-button value="localPath">守护进程本机路径</t-radio-button>
                </t-radio-group>
              </t-form-item>

              <t-form-item
                v-if="packageSourceType === 'upload'"
                label="上传服务端整合包 (Zip)"
                name="packageFileKey"
                class="!mb-0"
              >
                <div class="w-full sm:w-[32rem]">
                  <input ref="uploadInputRef" accept=".zip" type="file" style="display: none" @change="onFileChange" />

                  <t-button
                    v-if="!isUploading && !formData.packageFileKey"
                    variant="outline"
                    class="!w-full !justify-start !pl-4 !h-10 !bg-transparent border-zinc-200 dark:border-zinc-700 hover:!border-[var(--color-primary)]"
                    @click="triggerFileSelect"
                  >
                    <template #icon><t-icon name="upload" class="opacity-70" /></template>
                    点击选择 Zip 文件并上传
                  </t-button>

                  <div
                    v-if="isUploading"
                    class="w-full bg-transparent p-4 mt-4 rounded-lg border border-[var(--color-primary)]/40"
                  >
                    <div class="text-sm font-bold text-[var(--td-text-color-primary)] mb-2 truncate">
                      正在上传: {{ uploadedFileName }} ({{ uploadedFileSize }})
                    </div>
                    <t-progress theme="line" :percentage="uploadProgress" :label="`${Math.round(uploadProgress)}%`" />
                    <div class="text-[11px] text-zinc-500 mt-2 text-center">别着急，喝杯咖啡☕️...</div>
                  </div>

                  <div
                    v-if="!isUploading && isCheckingPackage"
                    class="w-full bg-transparent p-4 mt-4 rounded-lg border border-[var(--color-primary)]/40 flex items-center justify-center"
                  >
                    <t-loading text="正在分析压缩包结构..." size="small" />
                  </div>

                  <div
                    v-if="formData.packageFileKey && !isUploading && !isCheckingPackage"
                    class="flex items-center gap-3 mt-4 p-3 bg-transparent rounded-lg border border-[var(--color-success)]/40 relative overflow-hidden group"
                  >
                    <div class="absolute left-0 top-0 bottom-0 w-1 bg-[var(--color-success)] opacity-80"></div>
                    <t-icon name="folder-zip" class="text-[var(--color-success)] text-xl shrink-0 ml-1" />
                    <div class="flex-1 min-w-0">
                      <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                        {{ uploadedFileName }}
                      </div>
                      <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                        {{
                          detectedJars.length > 0
                            ? `发现 ${detectedJars.length} 个服务端核心文件`
                            : '未发现服务端核心文件'
                        }}
                        {{ detectedRoot ? `| 根目录: /${detectedRoot}` : '' }}
                      </div>
                    </div>
                    <div class="flex items-center gap-1 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                      <t-button shape="square" variant="text" theme="primary" @click="triggerFileSelect"
                        ><t-icon name="swap"
                      /></t-button>
                      <t-button
                        shape="square"
                        variant="text"
                        theme="danger"
                        class="hover:!bg-red-500/10"
                        @click="removeUploadedFile"
                        ><t-icon name="delete"
                      /></t-button>
                    </div>
                  </div>
                </div>
              </t-form-item>

              <t-form-item
                v-else
                label="本机绝对路径"
                name="packageLocalPath"
                class="!mb-0"
                help="请确保守护进程有权限读取该路径下的文件"
              >
                <div class="flex items-center gap-2 w-full sm:w-[32rem]">
                  <t-input
                    v-model="formData.packageLocalPath"
                    placeholder="例如: /data/mc/modpack.zip"
                    class="!font-mono !flex-1"
                    @change="
                      (val) => {
                        if (val) analyzePackage('', val as any);
                      }
                    "
                  />
                  <t-button variant="outline" @click="showHostFileSelector = true" class="shrink-0">
                    <template #icon><folder-open-icon /></template>
                    浏览
                  </t-button>
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 2" class="list-item-anim flex-1 pt-1">
              <div v-if="detectedJars.length > 0">
                <t-alert
                  theme="success"
                  class="!mb-6 !rounded-xl !bg-[var(--color-success)]/10 !border-[var(--color-success)]/20"
                >
                  <template #message
                    >我们在压缩包中发现了以下服务端核心文件，请选择哪一个作为<b>启动核心</b>。</template
                  >
                </t-alert>

                <t-form-item label="选择启动核心" name="core">
                  <t-radio-group v-model="formData.core" class="flex flex-col gap-3 w-full items-start">
                    <div v-for="jar in detectedJars" :key="jar" class="flex w-full items-center justify-start">
                      <t-radio :value="jar" class="!font-mono !text-sm">{{ jar }}</t-radio>
                    </div>
                  </t-radio-group>
                </t-form-item>
              </div>

              <div v-else>
                <t-alert theme="warning" class="!mb-6 !rounded-xl !bg-amber-500/10 !border-amber-500/20">
                  <template #message>在上传的包中未发现服务端核心文件。请在此处下载一个或等待创建后手动补充。</template>
                </t-alert>

                <t-form-item label="补充服务端核心" class="!mb-5">
                  <t-radio-group v-model="downloadType" variant="default-filled">
                    <t-radio-button value="online">在线下载核心</t-radio-button>
                    <t-radio-button disabled value="manual">自行上传(不支持)</t-radio-button>
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
                          打开核心库
                        </t-button>

                        <div
                          v-if="formData.core"
                          class="flex items-center gap-3 mt-4 p-3 bg-transparent rounded-lg border border-[var(--color-primary)]/40 shadow-sm relative overflow-hidden group"
                        >
                          <div class="absolute left-0 top-0 bottom-0 w-1 bg-[var(--color-primary)] opacity-80"></div>
                          <t-icon
                            name="check-circle-filled"
                            class="text-[var(--color-primary)] text-xl shrink-0 ml-1"
                          />
                          <div class="flex-1 min-w-0">
                            <div class="font-bold text-sm text-[var(--td-text-color-primary)] truncate">
                              {{ formData.core }}
                            </div>
                            <div class="text-[11px] text-[var(--td-text-color-secondary)] truncate mt-0.5">
                              将在创建时自动下载
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
                  </div>

                  <div v-if="downloadType === 'manual'" class="mt-2">
                    <div class="text-sm text-[var(--td-text-color-secondary)] mb-4">
                      请在整合包解压后手动放入核心，或在此处不填写等待创建后手动上传。
                    </div>
                    <t-alert
                      theme="error"
                      message="此模式下建议确保压缩包内包含核心，或者使用在线下载功能。"
                      class="!rounded-xl"
                    />
                  </div>
                </div>
              </div>
            </div>

            <div v-show="currentStep === 3" class="list-item-anim flex-1 pt-1">
              <t-alert v-if="recommendedJavaVersion" theme="success" :title="`推荐 Java 版本: Java ${recommendedJavaVersion}`" class="!mb-6 !rounded-xl">
                <template #message>
                  检测到您的游戏版本为 <span class="font-bold text-[var(--color-success)]">{{ detectedGameVersion }}</span>，已为您自动选择推荐的 Java {{ recommendedJavaVersion }}。
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
                      <t-icon name="info-circle" size="14px" /> 建议直接使用推荐版本，避免兼容性问题。
                    </div>
                  </div>
                </template>
              </t-alert>

              <t-form-item label="Java 来源" name="java" class="!mb-0">
                <div class="w-full">
                  <t-radio-group v-model="javaType" variant="default-filled" class="!mb-4">
                    <t-radio-button value="online">在线下载</t-radio-button>
                    <t-radio-button value="local">本机 Java</t-radio-button>
                    <t-radio-button value="env">环境变量</t-radio-button>
                    <t-radio-button value="custom">自定义路径</t-radio-button>
                    <t-radio-button value="docker">Docker 容器环境 🐳</t-radio-button>
                  </t-radio-group>

                  <div v-if="javaType === 'docker'" class="w-full sm:w-[32rem] min-h-[70px] mt-2">
                    <div
                      class="list-item-anim border border-zinc-200 dark:border-zinc-800 rounded-2xl p-4 bg-zinc-50/30 dark:bg-zinc-900/10 w-full"
                    >
                      <div class="mb-4">
                        <t-radio-group v-model="dockerImageType" size="small" variant="outline">
                          <t-radio-button value="preset">MSLX 官方运行时镜像</t-radio-button>
                          <t-radio-button value="custom">自定义公网镜像</t-radio-button>
                        </t-radio-group>
                      </div>

                      <div v-if="dockerImageType === 'preset'">
                        <div class="flex items-center gap-3">
                          <span class="text-xs font-bold text-zinc-500 shrink-0">运行环境:</span>
                          <t-select v-model="dockerImagePresetVersion" class="!w-48" :clearable="false">
                            <t-option :label="recommendedJavaVersion === '25' ? 'Java 25 (推荐)' : 'Java 25'" value="25" />
                            <t-option :label="recommendedJavaVersion === '21' ? 'Java 21 (推荐)' : 'Java 21'" value="21" />
                            <t-option :label="recommendedJavaVersion === '17' ? 'Java 17 (推荐)' : 'Java 17'" value="17" />
                            <t-option :label="recommendedJavaVersion === '11' ? 'Java 11 (推荐)' : 'Java 11'" value="11" />
                            <t-option :label="recommendedJavaVersion === '8' ? 'Java 8 (推荐)' : 'Java 8'" value="8" />
                          </t-select>
                        </div>
                      </div>

                      <div v-if="dockerImageType === 'custom'">
                        <t-input
                          v-model="formData.dockerImage"
                          placeholder="例如: eclipse-temurin:21-jre-jammy"
                          class="!font-mono"
                        />
                        <div class="text-[11px] text-amber-500 mt-2">
                          ⚠️ 提示：使用自定义公共镜像创建时，系统将通过终端执行标准 docker
                          pull，请确保网络通畅（如有需要请添加镜像源前缀）。
                        </div>
                      </div>
                    </div>
                  </div>

                  <div class="w-full sm:w-[32rem] min-h-[70px] mt-2">
                    <div v-if="javaType === 'online'">
                      <t-select
                        v-model="selectedJavaVersion"
                        :options="javaVersionsWithOptions"
                        placeholder="请选择 Java 版本"
                        class="!w-full sm:!w-64"
                      />
                    </div>
                    <div v-if="javaType === 'local'" class="flex items-center gap-3">
                      <t-select
                        v-model="customJavaPath"
                        :options="localJavaVersionsWithOptions"
                        placeholder="请选择 Java"
                        class="!flex-1"
                      />
                      <t-button variant="base" theme="default" @click="fetchJavaVersions(true)">刷新</t-button>
                    </div>
                    <div v-if="javaType === 'env'">
                      <t-input
                        model-value="java"
                        readonly
                        disabled
                        class="!font-mono !bg-zinc-100 dark:!bg-zinc-800/50"
                      />
                    </div>
                    <div v-if="javaType === 'custom'">
                      <t-input v-model="customJavaPath" placeholder="C:\Path\To\java.exe" class="!font-mono" />
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

              <div
                v-if="isDockerMode"
                class="col-span-1 sm:col-span-2 mt-4 pt-4 border-t border-dashed border-zinc-200 dark:border-zinc-800 w-full sm:w-[40rem] list-item-anim"
              >
                <div class="mb-4">
                  <span class="text-sm font-bold text-[var(--td-text-color-primary)] block mb-2">Docker 网络模式</span>
                  <t-radio-group v-model="networkMode" variant="default-filled" class="w-full">
                    <t-radio-button value="mapped">端口映射</t-radio-button>
                    <t-radio-button value="host">Host 网络模式 (共用宿主机网络)</t-radio-button>
                  </t-radio-group>
                </div>

                <template v-if="networkMode === 'mapped'">
                  <div class="flex items-center justify-between mb-3 mt-4">
                    <div class="flex flex-col">
                      <span class="text-sm font-bold text-[var(--td-text-color-primary)]">容器网络映射端口</span>
                      <span class="text-[11px] text-zinc-400 mt-0.5">绑定宿主机端口与协议，供玩家外部连接</span>
                    </div>
                    <t-button variant="outline" theme="primary" size="small" @click="addPortMapping">
                      <template #icon><t-icon name="add" /></template>增加端口映射
                    </t-button>
                  </div>

                  <div
                    v-if="localPortList.length === 0"
                    class="p-4 rounded-xl border border-dashed border-zinc-200 dark:border-zinc-700 text-center text-xs text-zinc-400"
                  >
                    未公开任何网络端口，请点击右上角增加端口映射
                  </div>

                  <div class="flex flex-col gap-3">
                    <div
                      v-for="(port, index) in localPortList"
                      :key="index"
                      class="flex items-center gap-3 list-item-anim"
                    >
                      <div class="flex-1 flex items-center gap-2 overflow-visible p-[2px] -m-[2px]">
                        <!-- 宿主机端口 -->
                        <t-input-number
                          v-model="port.host"
                          :min="1"
                          :max="65535"
                          placeholder="宿主机"
                          theme="column"
                          class="flex-1 min-w-0"
                        />
                        <span class="text-zinc-400 shrink-0 font-bold">:</span>
                        <!-- 容器内部端口 -->
                        <t-input-number
                          v-model="port.container"
                          :min="1"
                          :max="65535"
                          placeholder="容器内"
                          theme="column"
                          class="flex-1 min-w-0"
                        />

                        <!-- 协议选择下拉框 -->
                        <t-select v-model="port.protocol" size="medium" class="!w-[80px] shrink-0" :clearable="false">
                          <t-option label="TCP" value="tcp" />
                          <t-option label="UDP" value="udp" />
                        </t-select>
                      </div>

                      <!-- 删除按钮 -->
                      <t-button
                        shape="circle"
                        variant="text"
                        theme="danger"
                        size="small"
                        class="shrink-0 hover:!bg-red-500/10"
                        @click="removePortMapping(index)"
                      >
                        <t-icon name="delete" />
                      </t-button>
                    </div>
                  </div>
                </template>

                <div
                  v-else
                  class="p-4 rounded-xl border border-dashed border-zinc-200 dark:border-zinc-700 text-center text-xs text-zinc-400 mt-4"
                >
                  当前处于 Host 网络模式下，容器将共享宿主机的端口，无需额外配置端口映射
                </div>

                <!-- 隐藏的表单验证字段绑定 -->
                <t-form-item name="DockerPorts" class="!mb-0 !mt-2" v-show="false">
                  <t-input v-model="formData.dockerPorts" />
                </t-form-item>
              </div>

              <t-form-item label="JVM 参数" name="args" class="!mt-8 w-full sm:w-[40rem]">
                <t-textarea
                  v-model="formData.args"
                  placeholder="-XX:+UseG1GC"
                  :autosize="{ minRows: 3, maxRows: 6 }"
                  class="!font-mono !bg-transparent"
                />
              </t-form-item>
            </div>

            <div v-show="currentStep === 5" class="list-item-anim flex-1 pt-1">
              <div class="flex flex-col min-w-0 mb-8 pb-6 border-b border-zinc-200 dark:border-zinc-800">
                <div class="text-3xl font-extrabold text-[var(--td-text-color-primary)] truncate tracking-tight">
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
                    >服务端整合包</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        :title="packageSourceType === 'upload' ? uploadedFileName : formData.packageLocalPath"
                      >
                        {{ packageSourceType === 'upload' ? uploadedFileName : formData.packageLocalPath }}
                      </span>
                      <t-tag theme="primary" variant="light" size="small" class="!rounded">
                        {{ packageSourceType === 'upload' ? 'ZIP' : '本机路径' }}
                      </t-tag>
                    </div>
                    <div v-if="packageSourceType === 'upload'" class="text-[11px] text-zinc-500 mt-1">
                      大小: {{ uploadedFileSize || '未知' }}
                    </div>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >启动核心 (Jar)</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        :title="formData.core"
                        >{{ formData.core }}</span
                      >
                      <t-tag
                        v-if="detectedJars.length > 0"
                        theme="success"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >整合包内核心</t-tag
                      >
                      <t-tag
                        v-else-if="downloadType === 'online'"
                        theme="primary"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >在线下载</t-tag
                      >
                      <t-tag v-else theme="warning" variant="light" size="small" class="!rounded">手动配置</t-tag>
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-1 truncate">
                      <span v-if="detectedJars.length > 0">已从压缩包中选定启动文件</span>
                      <span v-else-if="downloadType === 'online'"
                        >来源: MSL 镜像源 ({{ formData.coreUrl ? '已匹配' : '未匹配' }})</span
                      >
                    </div>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0"
                    >运行时生态</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span v-if="javaType === 'online'" class="text-sm font-bold text-[var(--td-text-color-primary)]"
                        >Java {{ selectedJavaVersion }}</span
                      >
                      <span
                        v-else-if="javaType === 'docker' && dockerImageType === 'preset'"
                        class="text-sm font-bold text-[var(--td-text-color-primary)]"
                        >MSLX 容器运行时</span
                      >
                      <span
                        v-else-if="javaType === 'docker' && dockerImageType === 'custom'"
                        class="text-sm font-bold text-[var(--td-text-color-primary)]"
                        >自建隔离镜像生态</span
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
                      <t-tag
                        v-else-if="javaType === 'docker'"
                        theme="warning"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >沙盒隔离</t-tag
                      >
                      <t-tag
                        v-else-if="javaType === 'local'"
                        theme="primary"
                        variant="light"
                        size="small"
                        class="!rounded"
                        >本机环境</t-tag
                      >
                      <t-tag v-else theme="default" variant="light" size="small" class="!rounded">自定义</t-tag>
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-1 truncate max-w-[250px] sm:max-w-[350px]">
                      <span v-if="javaType === 'online'">将自动从镜像源下载并解压 JDK</span>
                      <span v-else-if="javaType === 'docker'"
                        >目标环境: {{ formData.dockerImage }} | 端口透传:
                        {{ formData.dockerPorts === '0' ? 'Host网络' : formData.dockerPorts }}</span
                      >
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
                  v-if="javaType === 'docker'"
                  class="flex flex-col sm:flex-row sm:items-start justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-1.5 sm:mb-0 shrink-0 mt-0.5"
                    >Docker 运行环境</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right w-full sm:max-w-md">
                    <div class="flex flex-wrap items-center sm:justify-end gap-2">
                      <span
                        class="text-xs font-mono font-bold bg-zinc-100 dark:bg-zinc-800 px-2 py-0.5 rounded text-zinc-700 dark:text-zinc-300 break-all"
                      >
                        {{ formData.dockerImage }}
                      </span>
                      <t-tag theme="warning" variant="light" size="small" class="!rounded">沙盒隔离</t-tag>
                    </div>
                    <div class="text-[11px] text-zinc-500 mt-2 font-mono">
                      端口放行透传:
                      <span class="text-[var(--color-primary)] font-bold">{{
                        formData.dockerPorts === '0' ? 'Host网络透传' : formData.dockerPorts
                      }}</span>
                    </div>
                  </div>
                </div>

                <div v-if="formData.args" class="flex flex-col sm:flex-row sm:items-start justify-between py-4">
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-2 sm:mb-0 shrink-0 mt-1"
                    >启动参数</span
                  >
                  <div
                    class="text-xs font-mono text-[var(--td-text-color-secondary)] break-all leading-relaxed bg-zinc-50/50 dark:bg-zinc-800/30 p-2.5 rounded-lg border border-zinc-100 dark:border-zinc-800 text-left sm:text-right max-w-full sm:max-w-md"
                  >
                    {{ formData.args }}
                  </div>
                </div>
              </div>

              <t-alert
                theme="info"
                class="!mt-8 !rounded-xl !bg-[var(--color-primary)]/5 !border-[var(--color-primary)]/20"
              >
                <template #message
                  >确认无误后点击下方
                  <strong class="text-[var(--color-primary)] mx-1">提交创建</strong>，系统自动部署服务端。</template
                >
              </t-alert>
            </div>

            <div class="mt-auto pt-6 border-t border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
              <t-button v-if="currentStep > 0 && currentStep < 6" theme="default" @click="prevStep">上一步</t-button>
              <div v-else></div>
              <t-button
                v-if="currentStep < 5"
                type="button"
                theme="primary"
                :loading="isUploading || isCheckingPackage"
                @click="nextStep"
                >下一步</t-button
              >
              <t-button v-if="currentStep === 5" theme="primary" type="submit" :loading="isSubmitting"
                >提交创建</t-button
              >
            </div>
          </t-form>
        </div>

        <div v-if="isCreating" class="h-full flex flex-col items-center justify-center py-8 list-item-anim">
          <div class="text-lg font-bold text-[var(--td-text-color-primary)] mb-2 tracking-tight">
            正在创建整合包实例 ({{ createdServerId }})
          </div>
          <p class="text-sm text-[var(--td-text-color-secondary)] mb-6">正在解压文件并配置环境...</p>

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
            整合包服务器已部署成功
          </div>

          <div class="text-sm text-[var(--td-text-color-secondary)] leading-[22px] !my-2 !mb-8">
            文件已解压，环境已配置就绪
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
    <host-file-selector v-model:visible="showHostFileSelector" search-pattern="*.zip" @select="onHostFileSelected" />
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
