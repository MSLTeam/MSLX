<script setup lang="ts">
import { onUnmounted, ref, watch, onMounted, computed, nextTick } from 'vue';
import { useRouter } from 'vue-router';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

import ServerCoreSelector from './ServerCoreSelector.vue';
import { getJavaVersionList } from '@/api/mslapi/java';
import { getLocalJavaList } from '@/api/localJava';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { initUpload, uploadChunk, finishUpload, deleteUpload } from '@/api/files';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';

// 状态管理
const userStore = useUserStore();
const router = useRouter();
const formRef = ref(null);

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
const downloadType = ref('online'); // 'online' | 'manual'
const showCoreSelector = ref(false);

// 上传相关状态
const uploadInputRef = ref<HTMLInputElement | null>(null);
const isUploading = ref(false);
const uploadProgress = ref(0);
const uploadedFileName = ref('');
const uploadedFileSize = ref('');

// Java 选择相关状态
const javaType = ref('online');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const localJavaVersions = ref<{ label: string; value: string }[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');

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
        selectedJavaVersion.value = javaVersions.value[1].value; // 默认java21
      }
    }
    localJavaVersions.value = (await getLocalJavaList(force)).map((v) => ({
      label: `Java ${v.version}${v.is64Bit ? '' : ' (32位)'} (${v.vendor} | ${v.path})`,
      value: v.path,
    }));
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
  fetchJavaVersions();
});

const formData = ref(<CreateInstanceQucikModeModel>{
  name: '新建服务器',
  path: '',
  java: '',
  core: '',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '', // 上传成功后的 Key
  packageFileKey: '',
  minM: 1024,
  maxM: 4096,
  args: '',
});

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

// 表单校验规则 (动态校验)
const FORM_RULES = computed<FormRules>(() => {
  return {
    name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
    java: [{ required: true, message: '请配置 Java 环境', trigger: 'change' }],
    // core 字段在手动上传时，我们用 coreFileKey 来判断逻辑，但为了表单显示，我们还是要求用户填/选个文件
    core: [{ required: true, message: '核心名称/文件不能为空', trigger: 'change' }],
    coreUrl: [
      {
        validator: (val) => {
          // 只有在线下载模式才校验 coreUrl
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
    // 上传文件的key校验
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
  ['java'],
  ['core', 'coreUrl', 'coreSha256', 'coreFileKey'],
  ['minM', 'maxM', 'args'],
];

// 步骤导航
const prevStep = () => {
  if (currentStep.value > 0) {
    currentStep.value -= 1;
  }
};

const nextStep = async () => {
  // 步骤2的特殊拦截
  if (currentStep.value === 2) {
    if (downloadType.value === 'online') {
      if (!formData.value.coreUrl || !formData.value.core) {
        MessagePlugin.warning('请点击按钮选择一个服务端核心');
        return;
      }
    } else {
      // 自定义文件
      if (!formData.value.coreFileKey) {
        MessagePlugin.warning('请先上传核心文件');
        return;
      }
    }
  }

  // 执行表单校验
  const validateResult = await formRef.value.validate();
  if (validateResult === true) {
    if (currentStep.value < 3) currentStep.value += 1;
    return;
  }

  // 检查当前步骤是否有错误
  const fieldsToValidate = new Set(stepValidationFields[currentStep.value]);
  const hasErrorInCurrentStep = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasErrorInCurrentStep) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    // 如果错误不在当前步骤（比如是后面的步骤），允许下一步
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
  }
};

// 处理核心选择组件回调 (在线下载)
const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = ''; // 清空上传 Key
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);

  formRef.value.validate({ fields: ['core', 'coreUrl'] });
};

// --- 上传文件  ---

// 触发文件选择
const triggerFileSelect = () => {
  uploadInputRef.value?.click();
};

const formatFileSize = (bytes: number) => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

// 文件选择变动处理
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
  const fileName = file.name;

  // 重置状态
  formData.value.core = fileName;
  formData.value.coreUrl = '';
  formData.value.coreSha256 = '';
  formData.value.coreFileKey = '';

  uploadedFileName.value = fileName;
  uploadedFileSize.value = formatFileSize(file.size);

  // 开始上传
  await handleUpload(file);

  // 清空 Input 允许重复选择同一文件
  input.value = '';
};

// 核心分片上传逻辑
const handleUpload = async (file: File) => {
  isUploading.value = true;
  uploadProgress.value = 0;
  const chunkSize = 5 * 1024 * 1024; // 5MB
  const totalChunks = Math.ceil(file.size / chunkSize);

  try {
    // 获取 Upload ID
    const initRes = await initUpload();
    const uploadId = (initRes as any).uploadId;

    if (!uploadId) throw new Error('无法获取上传凭证');

    // 循环上传分片
    for (let i = 0; i < totalChunks; i++) {
      const start = i * chunkSize;
      const end = Math.min(file.size, start + chunkSize);
      const chunk = file.slice(start, end);

      await uploadChunk(uploadId, i, chunk);

      // 更新进度
      uploadProgress.value = Math.floor(((i + 1) / totalChunks) * 100);
    }

    // 完成合并
    const finishRes = await finishUpload(uploadId, totalChunks);
    const finalKey = (finishRes as any).uploadId;

    // 赋值给表单
    formData.value.coreFileKey = finalKey;
    MessagePlugin.success('核心文件上传成功！');

    // 触发校验清除错误提示
    formRef.value.validate({ fields: ['core', 'coreFileKey'] });
  } catch (error: any) {
    console.error(error);
    MessagePlugin.error(`上传失败: ${error.message || '未知错误'}`);
    formData.value.core = '';
    uploadedFileName.value = '';
    uploadProgress.value = 0;
  } finally {
    isUploading.value = false;
  }
};

const removeUploadedFile = async () => {
  if (formData.value.coreFileKey) {
    await deleteUpload(formData.value.coreFileKey);
    formData.value.coreFileKey = '';
    formData.value.core = '';
    uploadedFileName.value = '';
    MessagePlugin.success('文件已移除');
  }
}
// --- 上传相关 结束 ---

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
    coreFileKey: formData.value.coreFileKey || null, // 确保传给后端
    args: formData.value.args || null,
  };

  // 再次确保：如果是手动上传模式，清除 Url 避免冲突
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
    currentStep.value = 4;

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

  if (!baseUrl || !token) {
    MessagePlugin.error('未找到登录信息 (baseUrl 或 token)，无法连接到实时进度服务。');
    isCreating.value = false;
    isSubmitting.value = false;
    currentStep.value = 0;
    return;
  }

  let isSuccessHandled = false;

  const hubUrl = new URL('/api/hubs/creationProgressHub', baseUrl);
  hubUrl.searchParams.append('x-api-key', token);

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.Information)
    .build();

  const addLog = (message: string, progress: number | null = null) => {
    statusMessages.value.push({
      time: new Date().toLocaleTimeString(),
      message,
      progress,
    });

    // 自动滚动底部
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
      MessagePlugin.success('服务器创建成功！');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSuccess.value = true;
      currentStep.value = 5;
      isSubmitting.value = false;
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
});

const goToHome = () => {
  isSuccess.value = false;
  currentStep.value = 0;
  formData.value = {
    ...formData.value,
    name: '新建服务器',
    core: '',
    coreUrl: '',
    coreSha256: '',
    path: '',
    args: '',
    coreFileKey: '',
  };
  uploadedFileName.value = '';
  downloadType.value = 'online';
  javaType.value = 'online';
  customJavaPath.value = '';
};

const viewDetails = () => {
  router.push('/detail/advanced');
};
</script>

<template>
  <t-card :bordered="false">
    <div class="main-layout-container">
      <div class="steps-aside">
        <t-steps layout="vertical" style="margin-top: 16px" :current="currentStep" status="process" readonly>
          <t-step-item title="基本信息" content="填写实例名称和路径" />
          <t-step-item title="Java 环境" content="配置 Java 运行时" />
          <t-step-item title="核心文件" content="指定核心文件及下载" />
          <t-step-item title="资源配置" content="设置内存与 JVM 参数" />
          <t-step-item title="创建实例" content="提交并等待创建" />
          <t-step-item title="完成" content="查看创建结果" />
        </t-steps>
      </div>

      <div class="main-content">
        <div v-if="!isCreating && !isSuccess" class="form-step-container">
          <t-form
            ref="formRef"
            :data="formData"
            :rules="FORM_RULES"
            label-align="top"
            class="step-form"
            @submit="onSubmit"
          >
            <div v-show="currentStep === 0" class="step-content">
              <t-form-item label="实例名称" name="name">
                <t-input v-model="formData.name" placeholder="为你的服务器起个名字" />
              </t-form-item>
              <t-form-item label="实例路径" name="path" help="选填，留空将使用默认路径">
                <t-input v-model="formData.path" placeholder="例如: D:\MyServer" />
              </t-form-item>
            </div>

            <div v-show="currentStep === 1" class="step-content">
              <t-alert theme="info" title="如何选择 Java 版本?" class="java-alert">
                <template #message>
                  <p>不同的 Minecraft 版本需要不同的 Java 版本。</p>
                  <ul>
                    <li>目前推荐最高使用 <b>Java 21</b> ，Java 25 可能存在兼容性问题。</li>
                    <li>MC 1.20.5 - 最新版本: 需要 Java 21 或更高版本。</li>
                    <li>MC 1.18 - 1.20.4: 需要 Java 17 或更高版本。</li>
                    <li>MC 1.17/1.17.1: 需要 Java 16。</li>
                    <li>MC 1.13 - 1.16.5: 需要 Java 8 / 11。</li>
                    <li>MC 1.12.2 及以下: 需要 Java 8。</li>
                  </ul>
                </template>
              </t-alert>

              <t-form-item label="Java 来源" name="java">
                <div style="width: 100%">
                  <t-radio-group v-model="javaType" variant="default-filled">
                    <t-radio-button value="online">在线下载</t-radio-button>
                    <t-radio-button value="local">选择电脑上的 Java</t-radio-button>
                    <t-radio-button value="env">环境变量</t-radio-button>
                    <t-radio-button value="custom">自定义路径</t-radio-button>
                  </t-radio-group>

                  <div class="java-option-panel">
                    <div v-if="javaType === 'online'" class="flex-row">
                      <t-select v-model="selectedJavaVersion" :options="javaVersions" placeholder="请选择 Java 版本" />
                      <div class="tip">
                        将下载并使用 Java {{ selectedJavaVersion || '?' }}
                        {{ userStore.userInfo.systemInfo.osType.toLowerCase().replace('os', '') }} /
                        {{ userStore.userInfo.systemInfo.osArchitecture.toLowerCase() }}
                      </div>
                    </div>

                    <div v-if="javaType === 'local'" class="flex-row">
                      <t-select v-model="customJavaPath" :options="localJavaVersions" placeholder="请选择 Java 版本" />
                      <t-button variant="outline" theme="primary" @click="fetchJavaVersions(true)">重新扫描</t-button>
                    </div>

                    <div v-if="javaType === 'env'">
                      <t-input model-value="java" readonly disabled />
                      <div class="tip">将使用系统环境变量中的 java 命令</div>
                    </div>

                    <div v-if="javaType === 'custom'">
                      <t-input v-model="customJavaPath" placeholder="例如: C:\Program Files\Java\jdk-17\bin\java.exe" />
                    </div>
                  </div>
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 2" class="step-content">
              <t-form-item label="选择您的Minecraft开服使用的服务端核心">
                <t-radio-group v-model="downloadType" variant="default-filled">
                  <t-radio-button value="online">在线下载 (推荐)</t-radio-button>
                  <t-radio-button value="manual">选择本地文件</t-radio-button>
                </t-radio-group>
              </t-form-item>

              <div v-if="downloadType === 'online'" class="online-select-area">
                <t-form-item label="选择服务端核心" name="coreUrl">
                  <div class="select-core-wrapper">
                    <t-button variant="outline" @click="showCoreSelector = true">
                      <template #icon><t-icon name="cloud-download" /></template>
                      点击打开服务端核心选择库
                    </t-button>

                    <div v-if="formData.core" class="selected-core-card">
                      <div class="core-icon"><t-icon name="check-circle-filled" /></div>
                      <div class="core-info">
                        <div class="core-filename">{{ formData.core }}</div>
                        <div class="core-url" title="MSLX 将在稍后帮您自动下载此文件...">
                          MSLX 将在稍后帮您自动下载此文件...
                        </div>
                      </div>
                      <t-button
                        shape="circle"
                        variant="text"
                        theme="danger"
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

              <div v-if="downloadType === 'manual'" class="online-select-area">
                <t-form-item label="上传核心文件" name="coreFileKey">
                  <div class="select-core-wrapper">
                    <input ref="uploadInputRef" accept=".jar" type="file" style="display: none" @change="onFileChange" />

                    <t-button v-if="!isUploading && !formData.coreFileKey" variant="outline" @click="triggerFileSelect">
                      <template #icon><t-icon name="upload" /></template>
                      点击选择文件并上传
                    </t-button>

                    <div v-if="isUploading" class="uploading-state">
                      <div class="core-filename">正在上传: {{ uploadedFileName }} ({{ uploadedFileSize }})</div>
                      <t-progress theme="line" :percentage="uploadProgress" />
                      <div class="tip">别着急，喝杯茶🍵...</div>
                    </div>

                    <div v-if="formData.coreFileKey && !isUploading" class="selected-core-card">
                      <div class="core-icon"><t-icon name="check-circle-filled" /></div>
                      <div class="core-info">
                        <div class="core-filename">{{ uploadedFileName }}</div>
                        <div class="core-url">{{ uploadedFileSize }} | 已上传准备就绪</div>
                      </div>
                      <t-button shape="circle" variant="text" theme="primary" @click="triggerFileSelect">
                        <template #icon><t-icon name="swap" /></template>
                      </t-button>

                      <t-button shape="circle" variant="text" theme="danger" @click="removeUploadedFile">
                        <template #icon><t-icon name="delete" /></template>
                      </t-button>
                    </div>
                  </div>
                </t-form-item>
              </div>
            </div>

            <div v-show="currentStep === 3" class="step-content">
              <t-row :gutter="16">
                <t-col :span="6">
                  <t-form-item label="最小内存 (MB)" name="minM">
                    <t-input-number v-model="formData.minM" :min="1" />
                  </t-form-item>
                </t-col>
                <t-col :span="6">
                  <t-form-item label="最大内存 (MB)" name="maxM">
                    <t-input-number v-model="formData.maxM" :min="1" />
                  </t-form-item>
                </t-col>
              </t-row>
              <t-form-item label="额外 JVM 参数 (可选)" name="args" help="例如: -XX:+UseG1GC">
                <t-textarea v-model="formData.args" placeholder="-XX:+UseG1GC" />
              </t-form-item>
            </div>

            <t-form-item class="step-actions">
              <t-button v-if="currentStep > 0" theme="default" @click="prevStep">上一步</t-button>
              <t-button v-if="currentStep < 3" type="button" @click="nextStep">下一步</t-button>
              <t-button v-if="currentStep === 3" theme="primary" type="submit" :loading="isSubmitting">
                提交创建
              </t-button>
            </t-form-item>
          </t-form>
        </div>

        <div v-if="isCreating" class="step-content creation-progress">
          <div class="progress-title">正在创建实例 ({{ createdServerId }})</div>
          <p>请勿关闭此页面，创建过程可能需要几分钟...</p>

          <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(2)}%`" />

          <div ref="logContainerRef" class="log-container">
            <t-list :split="true">
              <t-list-item v-for="(log, index) in statusMessages" :key="index">
                <t-list-item-meta>
                  <template #description>
                    <span class="log-time">[{{ log.time }}]</span>
                    <span class="log-message">{{ log.message }}</span>
                  </template>
                </t-list-item-meta>
              </t-list-item>
            </t-list>
          </div>
        </div>

        <div v-if="isSuccess" class="result-success">
          <t-icon class="result-success-icon" name="check-circle" />
          <div class="result-success-title">服务器 ({{ createdServerId }}) 已创建成功</div>
          <div class="result-success-describe">你现在可以去服务器列表启动它了</div>
          <div>
            <t-button @click="goToHome"> 返回 (创建新实例) </t-button>
            <t-button theme="default" @click="viewDetails"> 查看详情 (假) </t-button>
          </div>
        </div>
      </div>
    </div>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />
  </t-card>
</template>

<style scoped lang="less">
/* --- 布局样式 --- */
.main-layout-container {
  display: flex;
  gap: 32px;
}

.steps-aside {
  flex-shrink: 0;
  width: 220px;
  border-right: 1px solid var(--td-border-level-2-color);
  padding-right: 32px;
}

.main-content {
  flex-grow: 1;
  min-width: 0;
}

.form-step-container {
  padding-top: 0;
}

.step-content {
  margin: 0;
  padding: 16px 0;
}

.java-alert {
  margin-bottom: 24px;
}

.step-actions {
  margin-top: 32px;

  .t-button {
    margin-right: 16px;
  }
}

/* --- Java 面板样式 --- */
.java-option-panel {
  margin-top: 16px;
  padding: 16px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);

  .flex-row {
    display: flex;
    align-items: center;
    gap: 12px;
  }

  .tip {
    font-size: 12px;
    color: var(--td-text-color-secondary);
    margin-top: 8px;
    display: block;
  }
}

/* --- 核心选择器样式 --- */
.online-select-area {
  margin-top: 16px;
  padding: 16px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);
}

.select-core-wrapper {
  width: 100%;
}

.uploading-state {
  width: 100%;
  .core-filename {
    font-weight: 600;
    margin-bottom: 8px;
  }
  .tip {
    font-size: 12px;
    color: var(--td-text-color-secondary);
    margin-top: 4px;
  }
}

.selected-core-card {
  margin-top: 12px;
  display: flex;
  align-items: center;
  background-color: var(--td-bg-color-container);
  border: 1px solid var(--td-brand-color);
  border-radius: var(--td-radius-medium);
  padding: 12px;

  .core-icon {
    font-size: 24px;
    color: var(--td-brand-color);
    margin-right: 12px;
  }

  .core-info {
    flex: 1;
    overflow: hidden;

    .core-filename {
      font-weight: 600;
      color: var(--td-text-color-primary);
    }

    .core-url {
      font-size: 12px;
      color: var(--td-text-color-secondary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
  }
}

/* --- 进度条和结果页样式 --- */
.creation-progress {
  text-align: center;
  padding: 16px;

  .progress-title {
    font-size: 18px;
    font-weight: 500;
  }

  .t-progress {
    margin: 24px 0;
  }

  .log-container {
    margin-top: 24px;
    max-height: 400px;
    overflow-y: auto;
    text-align: left;
    background-color: var(--td-bg-color-container);
    border: 1px solid var(--td-border-level-2-color);
    border-radius: var(--td-radius-medium);

    .log-time {
      color: var(--td-text-color-placeholder);
      margin-right: 8px;
    }
  }
}

.result-success {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  min-height: 50vh;
  padding: 32px 0;

  &-icon {
    font-size: 64px;
    color: var(--td-success-color);
  }

  &-title {
    margin-top: 16px;
    font-size: 20px;
    color: var(--td-text-color-primary);
    text-align: center;
    line-height: 22px;
    font-weight: 500;
  }

  &-describe {
    margin: 8px 0 32px;
    font-size: 14px;
    color: var(--td-text-color-primary);
    line-height: 22px;
  }
}

@media (max-width: 768px) {
  .main-layout-container {
    flex-direction: column;
    gap: 24px;
  }

  .steps-aside {
    width: 100%;
    border-right: none;
    padding-right: 0;
    border-bottom: 1px solid var(--td-border-level-2-color);
    padding-bottom: 24px;
  }

  .step-content {
    max-width: 100%;
  }

  .result-success {
    min-height: 40vh;
  }
}
</style>
