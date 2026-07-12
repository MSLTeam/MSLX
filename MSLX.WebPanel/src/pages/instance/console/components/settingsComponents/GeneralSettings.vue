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

const normalizeRelativeInstancePath = (value: string | undefined, defaultPath: string) => {
  const normalized = (value || defaultPath).trim().replace(/\\/g, '/');
  if (!normalized) return defaultPath;
  return normalized.replace(/\/+/g, '/').replace(/^\.\//, '');
};

const isSafeRelativeInstancePath = (value: string | undefined, defaultPath: string) => {
  const normalized = normalizeRelativeInstancePath(value, defaultPath);
  if (/^[a-zA-Z]:\//.test(normalized) || normalized.startsWith('/')) return false;
  return normalized.split('/').every((segment) => segment && segment !== '.' && segment !== '..');
};

const serverPropertiesPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'server.properties'),
    message: 'server.properties 路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
];

const pluginsPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'plugins'),
    message: '插件目录路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
];

const modsPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'mods'),
    message: '模组目录路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
];

const worldPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'world'),
    message: '地图目录路径必须是实例目录内的相对路径',
    trigger: 'blur',
  },
];

const regionPathRules: FormRules[string] = [
  {
    validator: (val: string) => isSafeRelativeInstancePath(val, 'region'),
    message: 'Region 目录路径必须是地图目录内的相对路径',
    trigger: 'blur',
  },
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
  serverPropertiesPath: 'server.properties',
  pluginsPath: 'plugins',
  modsPath: 'mods',
  worldPath: 'world',
  regionPath: 'region',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
  // ====== docker ======
  dockerImage: 'MSLX://DockerImage/Java/25',
  dockerWorkingDir: '/mslx-data',
  dockerVolumes: '',
  dockerEnvVars: '',
  dockerNetworkMode: 'bridge',
  dockerNetworkAlias: '',
  dockerPorts: '',
  dockerCpuPercentage: undefined,
  dockerCpuCores: '',
  dockerMaxMemoryMb: undefined,
  dockerMaxSwapMb: undefined,
  dockerMaxStorage: '',
  dockerUploadRate: '',
  dockerDownloadRate: '',
  dockerExtraArgs: '',
  dockerExtraHosts: '',
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

// --- MCDReforged 逻辑 ---
// MCDR 使用java=“none” 即自定义模式
const mcdrPython = ref('python');
const initialized = ref(false); // 首次加载完成前不套用 MCDR 预设，避免覆盖已加载配置
const isMcdr = computed(() => javaType.value === 'mcdr');
const isCustomLike = computed(() => javaType.value === 'none' || javaType.value === 'mcdr');
// 仅 Java 模式才显示的区块(核心/内存/外置登录/强制UTF8)
const showJavaOnly = computed(() => {
  return (
    javaType.value === 'online' ||
    javaType.value === 'local' ||
    javaType.value === 'custom' ||
    javaType.value === 'env' ||
    javaType.value === 'docker-java'
  );
});
const mcdrLaunchCommand = computed(() => {
  const py = (mcdrPython.value || 'python').trim();
  const quoted = py.includes(' ') && !py.startsWith('"') ? `"${py}"` : py;
  return `${quoted} -m mcdreforged start`;
});

// 切换到 MCDR 模式时自动修改一些参数以适配
const applyMcdrDefaults = () => {
  formData.value.stopCommand = 'stop';
  formData.value.monitorPlayers = true;
  formData.value.inputEncoding = 'utf-8';
  formData.value.outputEncoding = 'utf-8';
  if (formData.value.serverPropertiesPath === 'server.properties')
    formData.value.serverPropertiesPath = 'server/server.properties';
  if (formData.value.pluginsPath === 'plugins') formData.value.pluginsPath = 'server/plugins';
  if (formData.value.modsPath === 'mods') formData.value.modsPath = 'server/mods';
  if (formData.value.worldPath === 'world') formData.value.worldPath = 'server/world';
};

// ====== Docker相关配置参数 ======
const expandedPanels = ref([]);
const isDockerMode = computed(() => {
  return javaType.value === 'docker-java' || javaType.value === 'docker-custom';
});

// 内置网络的 bridge, host, none 强制禁用网络别名
const isNetworkAliasDisabled = computed(() => {
  const mode = formData.value.dockerNetworkMode?.trim().toLowerCase() || 'bridge';
  return ['bridge', 'host', 'none'].includes(mode);
});

// 提取当前内置运行时伪协议中的 Java 版本号
const currentDockerJavaVersion = computed({
  get: () => {
    if (formData.value.dockerImage?.startsWith('MSLX://DockerImage/Java/')) {
      return formData.value.dockerImage.replace('MSLX://DockerImage/Java/', '');
    }
    return '21'; // 默认回显 21
  },
  set: (version) => {
    formData.value.dockerImage = `MSLX://DockerImage/Java/${version}`;
  },
});

// 容器内工作目录选择（默认 /mslx-data）
const isCustomWorkDir = ref(false);
watch(
  () => formData.value.dockerWorkingDir,
  (nv) => {
    isCustomWorkDir.value = nv !== '/mslx-data';
  },
);

// 端口映射解析器 (25565:25565 -> [{host:'', container:''}])
const portList = ref<{ host: string; container: string }[]>([]);
watch(
  () => formData.value.dockerPorts,
  (nv) => {
    if (!nv || nv === '0') {
      portList.value = [];
      return;
    }
    portList.value = nv.split(',').map((p) => {
      const [h, c] = p.split(':');
      return { host: h || '', container: c || '' };
    });
  },
  { immediate: true },
);

const updatePortsString = () => {
  formData.value.dockerPorts = portList.value
    .filter((p) => p.host && p.container)
    .map((p) => `${p.host}:${p.container}`)
    .join(',');
};
const addPortRow = () => {
  portList.value.push({ host: '', container: '' });
};
const removePortRow = (index: number) => {
  portList.value.splice(index, 1);
  updatePortsString();
};

// 目录挂载解析器 (/a:/b -> [{host:'', container:''}])
const volumeList = ref<{ host: string; container: string }[]>([]);
watch(
  () => formData.value.dockerVolumes,
  (nv) => {
    if (!nv) {
      volumeList.value = [];
      return;
    }
    volumeList.value = nv.split(',').map((v) => {
      const [h, c] = v.split(':');
      return { host: h || '', container: c || '' };
    });
  },
  { immediate: true },
);

const updateVolumesString = () => {
  formData.value.dockerVolumes = volumeList.value
    .filter((v) => v.host && v.container)
    .map((v) => `${v.host}:${v.container}`)
    .join(',');
};
const addVolumeRow = () => {
  volumeList.value.push({ host: '', container: '' });
};
const removeVolumeRow = (index: number) => {
  volumeList.value.splice(index, 1);
  updateVolumesString();
};

// 环境变量列表解析器 (A=1 -> [{key:'', value:''}])
const envList = ref<{ key: string; value: string }[]>([]);
watch(
  () => formData.value.dockerEnvVars,
  (nv) => {
    if (!nv) {
      envList.value = [];
      return;
    }
    envList.value = nv.split(',').map((e) => {
      const [k, v] = e.split('=');
      return { key: k || '', value: v || '' };
    });
  },
  { immediate: true },
);

const updateEnvString = () => {
  formData.value.dockerEnvVars = envList.value
    .filter((e) => e.key)
    .map((e) => `${e.key}=${e.value}`)
    .join(',');
};
const addEnvRow = () => {
  envList.value.push({ key: '', value: '' });
};
const removeEnvRow = (index: number) => {
  envList.value.splice(index, 1);
  updateEnvString();
};

// 额外 Hosts 列表解析器 (host.internal:127.0.0.1 -> [{domain:'', ip:''}])
const hostList = ref<{ domain: string; ip: string }[]>([]);
watch(
  () => formData.value.dockerExtraHosts,
  (nv) => {
    if (!nv) {
      hostList.value = [];
      return;
    }
    hostList.value = nv.split(',').map((h) => {
      const [d, i] = h.split(':');
      return { domain: d || '', ip: i || '' };
    });
  },
  { immediate: true },
);

const updateHostsString = () => {
  formData.value.dockerExtraHosts = hostList.value
    .filter((h) => h.domain && h.ip)
    .map((h) => `${h.domain}:${h.ip}`)
    .join(',');
};
const addHostRow = () => {
  hostList.value.push({ domain: '', ip: '' });
};
const removeHostRow = (index: number) => {
  hostList.value.splice(index, 1);
  updateHostsString();
};

watch(
  () => formData.value.dockerPorts,
  (newPorts) => {
    if (newPorts === '0') {
      formData.value.dockerNetworkMode = 'host';
      formData.value.dockerNetworkAlias = ''; // host 模式下强制清空别名
    } else if (formData.value.dockerNetworkMode === 'host') {
      formData.value.dockerNetworkMode = 'bridge';
    }
  },
);

// docker end~

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
  if (type === 'none' || type === 'mcdr') formData.value.java = 'none';
  else if (type === 'env') formData.value.java = 'java';
  else if (type === 'docker-java' || type === 'docker-custom') formData.value.java = type;
  else if (type === 'custom' || type === 'local') formData.value.java = path;
  else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';
});

// MCDR 输出完整启动指令到args
watch([javaType, mcdrPython], ([type]) => {
  if (type === 'mcdr') {
    formData.value.args = mcdrLaunchCommand.value;
  }
});

// 切换到MCDR时配置参数
watch(javaType, (nv, ov) => {
  if (initialized.value && nv === 'mcdr' && ov !== 'mcdr') {
    applyMcdrDefaults();
  }
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
  // 自定义命令 / MCDR 模式，跳过核心和内存的校验
  if (isCustomLike.value) {
    return {
      name: [{ required: true, message: '服务器名称不能为空', trigger: 'blur' }],
      base: [{ required: true, message: '基础路径不能为空', trigger: 'blur' }],
      args: [{ required: true, message: '自定义模式必须填写启动命令', trigger: 'blur' }],
      serverPropertiesPath: serverPropertiesPathRules,
      pluginsPath: pluginsPathRules,
      modsPath: modsPathRules,
      worldPath: worldPathRules,
      regionPath: regionPathRules,
      dockerWorkingDir: [
        { required: true, message: 'Docker 工作目录不能为空', trigger: 'blur' },
        {
          validator: (val: string) => val.startsWith('/'),
          message: '必须是容器内部绝对路径，以 / 开头',
          trigger: 'blur',
        },
      ],
      dockerVolumes: [
        {
          pattern: /^([^:]+:[^:]+)(,[^:]+:[^:]+)*$/,
          message: '格式不正确，应为 /宿主机路径:/容器内路径',
          trigger: 'blur',
        },
      ],
      dockerEnvVars: [
        { pattern: /^([^=]+=[^,]*)(,[^=]+=[^,]*)*$/, message: '格式不正确，应为 KEY=VALUE，逗号隔开', trigger: 'blur' },
      ],
      dockerPorts: [
        {
          pattern: /^(0|^([0-9]+:[0-9]+)(,[0-9]+:[0-9]+)*)$/,
          message: '格式应为 0 或 宿主机端口:容器端口',
          trigger: 'blur',
        },
      ],
      dockerCpuCores: [{ pattern: /^[0-9,-]+$/, message: '仅支持数字、逗号或连字符，如 0-3', trigger: 'blur' }],
      dockerMaxStorage: [{ pattern: /^[0-9]+[gGmMkK]$/, message: '格式不正确，如 10g 或 500m', trigger: 'blur' }],
    };
  }

  return {
    name: [{ required: true, message: '服务器名称不能为空', trigger: 'blur' }],
    base: [{ required: true, message: '基础路径不能为空', trigger: 'blur' }],
    java: [{ required: true, message: 'Java 环境不能为空', trigger: 'change' }],
    core: [{ required: true, message: '核心文件名不能为空', trigger: 'change' }],
    minM: [{ required: true, message: '必填', trigger: 'blur' }],
    maxM: [{ required: true, message: '必填', trigger: 'blur' }],
    serverPropertiesPath: serverPropertiesPathRules,
    pluginsPath: pluginsPathRules,
    modsPath: modsPathRules,
    worldPath: worldPathRules,
    regionPath: regionPathRules,
    dockerWorkingDir: [
      { required: true, message: 'Docker 工作目录不能为空', trigger: 'blur' },
      {
        validator: (val: string) => val.startsWith('/'),
        message: '必须是容器内部绝对路径，以 / 开头',
        trigger: 'blur',
      },
    ],
    dockerVolumes: [
      {
        pattern: /^([^:]+:[^:]+)(,[^:]+:[^:]+)*$/,
        message: '格式不正确，应为 /宿主机路径:/容器内路径',
        trigger: 'blur',
      },
    ],
    dockerEnvVars: [
      { pattern: /^([^=]+=[^,]*)(,[^=]+=[^,]*)*$/, message: '格式不正确，应为 KEY=VALUE，逗号隔开', trigger: 'blur' },
    ],
    dockerPorts: [
      {
        pattern: /^(0|^([0-9]+:[0-9]+)(,[0-9]+:[0-9]+)*)$/,
        message: '格式应为 0 或 宿主机端口:容器端口',
        trigger: 'blur',
      },
    ],
    dockerCpuCores: [{ pattern: /^[0-9,-]+$/, message: '仅支持数字、逗号或连字符，如 0-3', trigger: 'blur' }],
    dockerMaxStorage: [{ pattern: /^[0-9]+[gGmMkK]$/, message: '格式不正确，如 10g 或 500m', trigger: 'blur' }],
  };
});

const initData = async () => {
  if (!instanceId.value) {
    return;
  }
  loading.value = true;
  initialized.value = false;
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
      serverPropertiesPath: normalizeRelativeInstancePath(res.serverPropertiesPath, 'server.properties'),
      pluginsPath: normalizeRelativeInstancePath(res.pluginsPath, 'plugins'),
      modsPath: normalizeRelativeInstancePath(res.modsPath, 'mods'),
      worldPath: normalizeRelativeInstancePath(res.worldPath, 'world'),
      regionPath: normalizeRelativeInstancePath(res.regionPath, 'region'),
    };

    // 判断内存单位
    minUnit.value = res.minM > 0 && res.minM % 1024 === 0 ? 'GB' : 'MB';
    maxUnit.value = res.maxM > 0 && res.maxM % 1024 === 0 ? 'GB' : 'MB';

    isPathEditable.value = false;

    // 解析 Java 类型
    if (res.java === 'docker-java' || res.java === 'docker-custom') {
      javaType.value = res.java;
    } else if (res.java === 'none') {
      if ((res.args ?? '').includes('mcdreforged')) {
        javaType.value = 'mcdr';
        const m = (res.args ?? '').match(/^\s*"?([^"]+?)"?\s+-m\s+mcdreforged/);
        mcdrPython.value = m ? m[1].trim() : 'python';
      } else {
        javaType.value = 'none';
      }
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
    initialized.value = true;
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

  formData.value.serverPropertiesPath = normalizeRelativeInstancePath(
    formData.value.serverPropertiesPath,
    'server.properties',
  );
  formData.value.pluginsPath = normalizeRelativeInstancePath(formData.value.pluginsPath, 'plugins');
  formData.value.modsPath = normalizeRelativeInstancePath(formData.value.modsPath, 'mods');
  formData.value.worldPath = normalizeRelativeInstancePath(formData.value.worldPath, 'world');
  formData.value.regionPath = normalizeRelativeInstancePath(formData.value.regionPath, 'region');

  // 自定义 / MCDR 模式下，不需要检查核心变更
  if (!isCustomLike.value) {
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

  // 处理自定义 / MCDR 模式的magic number
  if (isCustomLike.value) {
    formData.value.core = 'none';
    formData.value.minM = 1027; // magic number
    formData.value.maxM = 1102; // magic number
    formData.value.java = 'none';
    // 清空下载参数
    formData.value.coreUrl = '';
    formData.value.coreFileKey = '';
    formData.value.coreSha256 = '';
    // MCDR确定启动指令
    if (isMcdr.value) {
      formData.value.args = mcdrLaunchCommand.value;
    }
  }
  if (javaType.value === 'docker-java' || javaType.value === 'docker-custom') {
    formData.value.java = javaType.value;
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
              选择使用 Java 启动 Minecraft，或使用自定义命令启动其他程序 (如 Bedrock, Python
              等)，又或是使用Docker进行容器化。
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
                { label: 'Docker MSLX 内置运行时 (使用容器推荐这个)', value: 'docker-java' },
                { label: 'Docker 自定义容器', value: 'docker-custom' },
                { label: 'MCDReforged (MCDR)', value: 'mcdr' },
                { label: '自定义命令 (无Java)', value: 'none' },
              ]"
            />
            <div v-if="showJavaOnly" class="w-full">
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
            <div v-if="javaType === 'docker-java'" class="w-full">
              <t-select
                v-model="currentDockerJavaVersion"
                :options="[
                  { label: 'MSLX Docker镜像 [Java 8]', value: '8' },
                  { label: 'MSLX Docker镜像 [Java 11]', value: '11' },
                  { label: 'MSLX Docker镜像 [Java 17]', value: '17' },
                  { label: 'MSLX Docker镜像 [Java 21]', value: '21' },
                  { label: 'MSLX Docker镜像 [Java 25]', value: '25' },
                ]"
                placeholder="请选择轻量容器内 Java 运行时版本"
              />
            </div>
          </div>
        </div>

        <!-- MCDR：Python 命令 -->
        <div
          v-if="isMcdr"
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">MCDR 启动 (Python)</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              运行 MCDReforged 的 Python 命令或路径。真实服务端的 Java / 内存 / 核心请在
              <code class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded">config.yml</code> 中配置。
              <br />请确保下方路径指向
              <code class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded">server/</code> 子目录。
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
            <t-input
              v-model="mcdrPython"
              placeholder="例如: python3 或 C:\Python312\python.exe"
              class="w-full !font-mono"
            />
            <div class="text-[11px] text-zinc-400 break-all">
              实际启动命令: <span class="font-mono text-[var(--color-primary)]">{{ mcdrLaunchCommand }}</span>
            </div>
          </div>
        </div>

        <!-- 启动命令 / JVM 参数 (非 MCDR) -->
        <div
          v-if="!isMcdr"
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
              {{ javaType === 'none' || javaType === 'docker-custom' ? '启动命令 (Command)' : '启动参数 (JVM Args)' }}
            </div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              {{
                javaType === 'none' || javaType === 'docker-custom'
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

        <template v-if="isDockerMode">
          <div
            class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-dashed border-zinc-200/60 dark:border-zinc-700/60"
          >
            <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
            <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">Docker 容器设置</h2>
          </div>

          <div
            class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
          >
            <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
              <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">容器基础镜像</div>
              <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                当前运行实例所绑定的底层镜像生态。
              </div>
            </div>
            <div class="w-full md:w-[340px] shrink-0">
              <t-input
                v-model="formData.dockerImage"
                :disabled="javaType === 'docker-java'"
                placeholder="如 eclipse-temurin:21-jre"
                class="w-full !font-mono"
              />
            </div>
          </div>

          <div
            class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
          >
            <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
              <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">外部网络端口放行</div>
              <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                将服务端口暴露给外界。切换为 Host
                将共用物理机网络生态。格式为宿主机端口-容器内端口。（不知道什么意思就写一样的得了）
              </div>
            </div>
            <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
              <t-radio-group v-model="formData.dockerPorts" variant="default-filled" class="w-full">
                <t-radio-button value="25565:25565">端口映射</t-radio-button>
                <t-radio-button value="0">Host网络模式</t-radio-button>
              </t-radio-group>

              <template v-if="formData.dockerPorts !== '0'">
                <div
                  v-for="(item, idx) in portList"
                  :key="idx"
                  class="flex items-center gap-1.5 mt-1 bg-zinc-50 dark:bg-zinc-800/40 p-1.5 rounded-lg border border-zinc-200/50 dark:border-zinc-700/50"
                >
                  <t-input
                    v-model="item.host"
                    size="small"
                    placeholder="宿主机公开端口"
                    class="!font-mono text-xs flex-1"
                    @blur="updatePortsString"
                  />
                  <span class="text-zinc-400 font-bold">:</span>
                  <t-input
                    v-model="item.container"
                    size="small"
                    placeholder="容器端口"
                    class="!font-mono text-xs flex-1"
                    @blur="updatePortsString"
                  />
                  <t-button variant="text" theme="danger" shape="square" size="small" @click="removePortRow(idx)">
                    <template #icon><t-icon name="delete" /></template>
                  </t-button>
                </div>
                <t-button
                  dash
                  block
                  variant="outline"
                  size="small"
                  class="!rounded-lg text-xs mt-1"
                  @click="addPortRow"
                >
                  <template #icon><t-icon name="add" /></template>添加自定义映射端口
                </t-button>
              </template>
            </div>
          </div>

          <div class="mt-4 px-1">
            <t-collapse v-model="expandedPanels" borderless class="!bg-transparent">
              <t-collapse-panel value="advanced-docker" header="Docker 容器高级参数选项 (网络、目录挂载与算力限额)">
                <div class="flex flex-col mt-2">
                  <t-alert theme="info">
                    这里是Docker容器的高级配置，修改这里的配置时请您确保您知道您在做什么，否则不建议随意修改哦~
                  </t-alert>
                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器工作目录
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        容器内部的数据挂载绝对目录，默认为
                        <code class="font-mono bg-zinc-100 dark:bg-zinc-800 px-1 rounded">/mslx-data</code>。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
                      <t-radio-group v-model="isCustomWorkDir" variant="default-filled" class="w-full">
                        <t-radio-button :value="false">默认 (/mslx-data)</t-radio-button>
                        <t-radio-button :value="true">自定义路径</t-radio-button>
                      </t-radio-group>
                      <t-input
                        v-if="isCustomWorkDir"
                        v-model="formData.dockerWorkingDir"
                        placeholder="必须以 / 开头，例如 /my-server"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        Docker 网络拓扑模式
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        容器加入的虚拟网桥群。bridge / host / 自定义网络。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerNetworkMode"
                        :disabled="formData.dockerPorts === '0'"
                        placeholder="如 bridge 或 mslx-net"
                        class="w-full"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        内网集群服务解析别名
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        自定义隔离局域网内的通用容器代号，原生标准网桥下不可配置。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerNetworkAlias"
                        :disabled="isNetworkAliasDisabled"
                        :placeholder="isNetworkAliasDisabled ? '当前公共网桥拓扑下无法开启' : '输入微服务局域网别名'"
                        class="w-full"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        数据卷高级映射目录
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        服务器数据已默认隔离挂载，此处用于添加额外的外部宿主机数据卷。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
                      <div
                        v-for="(item, idx) in volumeList"
                        :key="idx"
                        class="flex items-center gap-1.5 bg-zinc-50 dark:bg-zinc-800/40 p-1.5 rounded-lg border border-zinc-200/50 dark:border-zinc-700/50"
                      >
                        <t-input
                          v-model="item.host"
                          size="small"
                          placeholder="宿主机绝对路径"
                          class="!font-mono text-xs flex-1"
                          @blur="updateVolumesString"
                        />
                        <span class="text-zinc-400 font-bold">:</span>
                        <t-input
                          v-model="item.container"
                          size="small"
                          placeholder="容器内部绝对路径"
                          class="!font-mono text-xs flex-1"
                          @blur="updateVolumesString"
                        />
                        <t-button
                          variant="text"
                          theme="danger"
                          shape="square"
                          size="small"
                          @click="removeVolumeRow(idx)"
                        >
                          <template #icon><t-icon name="delete" /></template>
                        </t-button>
                      </div>
                      <t-button
                        dash
                        block
                        variant="outline"
                        size="small"
                        class="!rounded-lg text-xs"
                        @click="addVolumeRow"
                      >
                        <template #icon><t-icon name="add" /></template>追加新数据挂载卷
                      </t-button>
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器 CPU 算力分配上限
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        限额本实例能调用的最高核心总比例。不设代表享用全额宿主机算力。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input-number
                        v-model="formData.dockerCpuPercentage"
                        :min="1"
                        :max="100"
                        placeholder="未设置代表不限制"
                        suffix="%"
                        class="w-full"
                        theme="column"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        核定绑定的物理 CPU 核心 (Cpuset)
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        将容器绑定在特定的硬件线程上运行，例如指定 <code class="font-mono text-xs">0,1</code> 或范围
                        <code class="font-mono text-xs">0-3</code>。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerCpuCores"
                        placeholder="不绑核请留空，例如 0-2"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器物理外部边界内存限制
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        Cgroups 物理内存封顶值。需大于 JVM 最大 Xmx。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input-number
                        v-model="formData.dockerMaxMemoryMb"
                        :min="4"
                        placeholder="无限制"
                        suffix="MB"
                        class="w-full"
                        theme="column"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器虚拟交换空间限制
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        允许容器爆内存时向外部磁盘借用的虚拟内存最大空间。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input-number
                        v-model="formData.dockerMaxSwapMb"
                        :min="0"
                        placeholder="无限制"
                        suffix="MB"
                        class="w-full"
                        theme="column"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器磁盘容量上限限制 <b>(仅Linux)</b>
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        强行截断当前容器内部最大可膨胀的物理空间，如 <code class="font-mono text-xs">15g</code>。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerMaxStorage"
                        placeholder="未设置代表不限制空间，例如 20g"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        网络出口最高上传吞吐限速 ()
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        限制本实例的最高网络实时上传速率，支持格式如 <code class="font-mono text-xs">500k</code>、<code
                          class="font-mono text-xs"
                          >500kb</code
                        >、<code class="font-mono text-xs">2mb</code> 或 <code class="font-mono text-xs">2mbps</code>。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerUploadRate"
                        placeholder="留空不限制，例如 5mb"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        网络入口最高下载吞吐限速
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        限制该实例下载资产时的实时网络带宽上限，格式同上。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerDownloadRate"
                        placeholder="留空不限制，例如 10mb"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        容器基础环境变量
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        为开服沙盒内置的环境注入，如指定时区或注入自定义密钥。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
                      <div
                        v-for="(item, idx) in envList"
                        :key="idx"
                        class="flex items-center gap-1.5 bg-zinc-50 dark:bg-zinc-800/40 p-1.5 rounded-lg border border-zinc-200/50 dark:border-zinc-700/50"
                      >
                        <t-input
                          v-model="item.key"
                          size="small"
                          placeholder="KEY"
                          class="!font-mono text-xs flex-1"
                          @blur="updateEnvString"
                        />
                        <span class="text-zinc-400 font-bold">=</span>
                        <t-input
                          v-model="item.value"
                          size="small"
                          placeholder="VALUE"
                          class="!font-mono text-xs flex-1"
                          @blur="updateEnvString"
                        />
                        <t-button variant="text" theme="danger" shape="square" size="small" @click="removeEnvRow(idx)">
                          <template #icon><t-icon name="delete" /></template>
                        </t-button>
                      </div>
                      <t-button
                        dash
                        block
                        variant="outline"
                        size="small"
                        class="!rounded-lg text-xs"
                        @click="addEnvRow"
                      >
                        <template #icon><t-icon name="add" /></template>新增环境变量
                      </t-button>
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        宿主机高级 Hosts 静态映射
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        让隔离容器连接宿主机底层接口，如配置
                        <code class="font-mono text-xs">host.mslx.internal:host-gateway</code>。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0 flex flex-col gap-2">
                      <div
                        v-for="(item, idx) in hostList"
                        :key="idx"
                        class="flex items-center gap-1.5 bg-zinc-50 dark:bg-zinc-800/40 p-1.5 rounded-lg border border-zinc-200/50 dark:border-zinc-700/50"
                      >
                        <t-input
                          v-model="item.domain"
                          size="small"
                          placeholder="域名 (如 db.local)"
                          class="!font-mono text-xs flex-1"
                          @blur="updateHostsString"
                        />
                        <span class="text-zinc-400 font-bold">-></span>
                        <t-input
                          v-model="item.ip"
                          size="small"
                          placeholder="IP"
                          class="!font-mono text-xs flex-1"
                          @blur="updateHostsString"
                        />
                        <t-button variant="text" theme="danger" shape="square" size="small" @click="removeHostRow(idx)">
                          <template #icon><t-icon name="delete" /></template>
                        </t-button>
                      </div>
                      <t-button
                        dash
                        block
                        variant="outline"
                        size="small"
                        class="!rounded-lg text-xs"
                        @click="addHostRow"
                      >
                        <template #icon><t-icon name="add" /></template>添加 Hosts 静态映射
                      </t-button>
                    </div>
                  </div>

                  <div
                    class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
                  >
                    <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
                      <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
                        Docker CLI 原生附加参数 (ExtraArgs)
                      </div>
                      <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
                        直接透传给系统底层的
                        <code class="font-mono text-xs">docker run</code> 命令，非专业高级定制请留空。
                      </div>
                    </div>
                    <div class="w-full md:w-[340px] shrink-0">
                      <t-input
                        v-model="formData.dockerExtraArgs"
                        placeholder="例如 --privileged 或 --dns=8.8.8.8"
                        class="w-full !font-mono"
                      />
                    </div>
                  </div>
                </div>
              </t-collapse-panel>
            </t-collapse>
          </div>
        </template>

        <template v-if="showJavaOnly">
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

        <template v-if="showJavaOnly">
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
          <div class="w-full md:w-[340px] shrink-0 flex items-center gap-3 p-[2px] -m-[2px]">
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

        <template v-if="showJavaOnly">
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
              设置在发出Stop指令或关服请求后，等待多久后强制结束进程<br />可设置10 - 300 s
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
          v-if="showJavaOnly"
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
          <div class="w-full md:w-[340px] shrink-0 flex items-center gap-3 p-[2px] -m-[2px]">
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

        <div class="border-b mt-8 border-dashed border-zinc-200/60 dark:border-zinc-700/60">
          <div class="flex items-center gap-2">
            <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
            <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">路径设置</h2>
          </div>
          <div class="text-xs text-[var(--td-text-color-secondary)] font-mono mt-1 pl-3 pt-1 pb-1">
            这里一般不需要改动，除非服务端路径不是根目录（如MCDR）。
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
              Server.properties 路径
            </div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对实例路径，用于读取端口、难度、游戏模式等服务端配置
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input
              v-model="formData.serverPropertiesPath"
              placeholder="server.properties 或 config/server.properties"
              class="w-full"
            />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">插件目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对实例路径，用于读取和管理服务端插件文件
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.pluginsPath" placeholder="plugins 或 custom/plugins" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">模组目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对实例路径，用于读取和管理服务端模组文件
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.modsPath" placeholder="mods 或 custom/mods" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">地图目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对实例路径，用于世界渲染图读取 level.dat 和 region 地图文件
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.worldPath" placeholder="world 或 saves/world" class="w-full" />
          </div>
        </div>

        <div
          class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl"
        >
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">Region 目录路径</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              相对地图目录路径，用于世界渲染图读取 r.x.z.mca 文件。默认 region；下界可填 DIM-1/region，末地可填
              DIM1/region
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex">
            <t-input v-model="formData.regionPath" placeholder="region 或 DIM-1/region" class="w-full" />
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

:deep(.t-input-number.w-full) {
  width: 100% !important;

  .t-input__wrap {
    width: 100% !important;
    flex: 1 1 auto !important;
  }

  .t-input {
    width: 100% !important;
  }
}
</style>
