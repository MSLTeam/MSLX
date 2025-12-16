<script setup lang="ts">
import { onUnmounted, ref, watch, onMounted, computed, nextTick } from 'vue';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

import ServerCoreSelector from './ServerCoreSelector.vue';
import { getJavaVersionList } from '@/api/mslapi/java';
import { getLocalJavaList } from '@/api/localJava';
import { postCreateInstanceQuickMode } from '@/api/instance';
import { initUpload, uploadChunk, finishUpload, deleteUpload, checkPackageJarList } from '@/api/files';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { useInstanceListStore } from '@/store/modules/instance';

// 状态管理
const userStore = useUserStore();
const formRef = ref(null);

const instanceListStore = useInstanceListStore();

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
const downloadType = ref('online'); // 'online' | 'manual' (仅在未检测到Jar时使用)
const showCoreSelector = ref(false);

// 整合包检测相关状态
const detectedJars = ref<string[]>([]);
const isCheckingPackage = ref(false);
const detectedRoot = ref('');

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
      if (javaVersions.value.length > 0 && !selectedJavaVersion.value) {
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
  minM: 2048,
  maxM: 6144,
  args: '',
});


// 处理内存单位
const minUnit = ref('GB');
const maxUnit = ref('GB');
const unitOptions = [
  { label: 'MB', value: 'MB' },
  { label: 'GB', value: 'GB' }
];

const minMComputed = computed({
  get: () => {
    return minUnit.value === 'GB' ? formData.value.minM / 1024 : formData.value.minM;
  },
  set: (val) => {
    // 存入 formData 时总是转回 MB
    formData.value.minM = minUnit.value === 'GB' ? Math.round(val * 1024) : val;
  }
});

const maxMComputed = computed({
  get: () => {
    return maxUnit.value === 'GB' ? formData.value.maxM / 1024 : formData.value.maxM;
  },
  set: (val) => {
    formData.value.maxM = maxUnit.value === 'GB' ? Math.round(val * 1024) : val;
  }
});

// 监听选择java的状态变量 修改表单数据
watch(
  [javaType, selectedJavaVersion, customJavaPath],
  ([type, ver, path]) => {
    if (type === 'env') formData.value.java = 'java';
    else if (type === 'custom' || type === 'local') formData.value.java = path;
    else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';

    if (formData.value.java) {
      formRef.value?.validate({ fields: ['java'] });
    }
  },
  { immediate: true },
);

// 表单和步骤校验
const FORM_RULES = computed<FormRules>(() => {
  return {
    name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
    // 整合包步骤校验
    packageFileKey: [{ required: true, message: '请上传整合包文件', trigger: 'change' }],
    // 核心步骤校验
    core: [
      {
        validator: (val) => {
          // 检测到了Jar，必须选中一个
          if (detectedJars.value.length > 0) {
            if (!val) return { result: false, message: '请选择一个启动Jar', type: 'error' };
          }
          // 没检测到Jar，回退到原逻辑
          else {
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
  };
});

const stepValidationFields = [
  ['name', 'path'], // Step 0: 基本信息
  ['packageFileKey'], // Step 1: 上传整合包
  ['core', 'coreUrl', 'coreFileKey'], // Step 2: 核心配置
  ['java'], // Step 3: Java
  ['minM', 'maxM', 'args'], // Step 4: 资源
];

const prevStep = () => {
  if (currentStep.value > 0) currentStep.value -= 1;
};

const nextStep = async () => {
  // Step 1 拦截: 必须上传完
  if (currentStep.value === 1) {
    if (!formData.value.packageFileKey) {
      MessagePlugin.warning('请先上传服务端整合包(Zip)');
      return;
    }
    if (isUploading.value || isCheckingPackage.value) {
      MessagePlugin.warning('请等待上传或分析完成');
      return;
    }
  }

  // Step 2 拦截: 必须确定核心
  if (currentStep.value === 2) {
    if (detectedJars.value.length > 0) {
      if (!formData.value.core) {
        MessagePlugin.warning('请从列表中选择一个启动核心');
        return;
      }
    } else {
      // 未检测到 Jar
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
    if (currentStep.value < 4) currentStep.value += 1;
    return;
  }

  // 检查当前步骤字段
  const fieldsToValidate = new Set(stepValidationFields[currentStep.value]);
  const hasError = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasError) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    if (currentStep.value < 4) currentStep.value += 1;
  }
};

// 上传和jar选择
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

const onFileChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;

  // 清理旧状态
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
  uploadedFileName.value = file.name;
  uploadedFileSize.value = formatFileSize(file.size);

  await handleUpload(file);
  input.value = '';
};

const handleUpload = async (file: File) => {
  isUploading.value = true;
  uploadProgress.value = 0;
  const chunkSize = 5 * 1024 * 1024; // 5MB
  const totalChunks = Math.ceil(file.size / chunkSize);

  try {
    const initRes = await initUpload();
    const uploadId = (initRes as any).uploadId;
    if (!uploadId) throw new Error('无法获取上传凭证');

    for (let i = 0; i < totalChunks; i++) {
      const start = i * chunkSize;
      const end = Math.min(file.size, start + chunkSize);
      await uploadChunk(uploadId, i, file.slice(start, end));
      uploadProgress.value = Math.floor(((i + 1) / totalChunks) * 100);
    }

    const finishRes = await finishUpload(uploadId, totalChunks);
    const finalKey = (finishRes as any).uploadId;

    formData.value.packageFileKey = finalKey;
    MessagePlugin.success('上传成功，正在分析整合包内容...');

    // 立即调用分析接口
    await analyzePackage(finalKey);
  } catch (error: any) {
    console.error(error);
    MessagePlugin.error(`上传失败: ${error.message || '未知错误'}`);
    uploadedFileName.value = '';
    uploadProgress.value = 0;
  } finally {
    isUploading.value = false;
  }
};

const analyzePackage = async (key: string) => {
  isCheckingPackage.value = true;
  try {
    const res = await checkPackageJarList(key);

    detectedJars.value = res.jars || [];
    detectedRoot.value = res.detectedRoot || '';

    if (res.count === 1 && res.jars.length > 0) {
      // 只有一个核心，自动选择
      formData.value.core = res.jars[0];
      MessagePlugin.success(`自动识别到服务端核心: ${res.jars[0]}`);
    } else if (res.count > 1) {
      MessagePlugin.info(`整合包内检测到 ${res.count} 个服务端核心，请在下一步选择`);
    } else {
      MessagePlugin.warning('未检测到整合包内存在服务端核心，请在下一步手动配置核心');
    }
  } catch (error: any) {
    MessagePlugin.error('整合包分析失败: ' + error.message);
  } finally {
    isCheckingPackage.value = false;
  }
};

const removeUploadedFile = async () => {
  if (formData.value.packageFileKey) {
    await deleteUpload(formData.value.packageFileKey);
    formData.value.packageFileKey = '';
    uploadedFileName.value = '';
    detectedJars.value = [];
    formData.value.core = '';
    MessagePlugin.success('文件已移除');
  }
};

// 核心选择
const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  formData.value.coreFileKey = '';
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);
};

// 提交和实时通讯
const onSubmit = async () => {
  const validateResult = await formRef.value.validate();
  if (validateResult !== true) {
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
  };

  // 清理不需要的字段
  if (detectedJars.value.length > 0) {
    apiData.coreUrl = null;
    apiData.coreSha256 = null;
    apiData.coreFileKey = null;
  } else {
    // 没检测到包，使用传统的下载/上传核心逻辑
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
    currentStep.value = 5; // 跳转到创建页

    await startSignalRConnection(createdServerId.value);
  } catch (error: any) {
    MessagePlugin.error(error.message || '创建请求失败');
    isSubmitting.value = false;
  }
};

const startSignalRConnection = async (serverId: string) => {
  const { baseUrl, token } = userStore;
  if (!baseUrl || !token) return;

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

    // 自动滚动底部
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
      currentStep.value = 6;
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

/*
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
    minM: 2048,
    maxM: 6144,
    args: '',
  };
  detectedJars.value = [];
  uploadedFileName.value = '';
  downloadType.value = 'online';
  javaType.value = 'online';
};

const viewDetails = () => {
  router.push('/detail/advanced');
}; */
</script>

<template>
  <t-card :bordered="false">
    <div class="main-layout-container">
      <div class="steps-aside">
        <t-steps layout="vertical" style="margin-top: 16px" :current="currentStep" status="process" readonly>
          <t-step-item title="基本信息" content="填写实例名称" />
          <t-step-item title="上传整合包" content="上传服务端 Zip 包" />
          <t-step-item title="核心配置" content="确认启动的服务端核心" />
          <t-step-item title="Java 环境" content="配置 Java 运行时" />
          <t-step-item title="资源配置" content="设置内存参数" />
          <t-step-item title="创建实例" content="提交并等待解压" />
          <t-step-item title="完成" content="查看结果" />
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
              <t-alert theme="info" class="mb-4">
                <template #message>
                  请上传包含服务端文件的 <b>.zip</b> 压缩包。上传完成后系统将自动分析包内的服务端核心文件。
                </template>
              </t-alert>

              <t-form-item label="上传服务端整合包 (Zip)" name="packageFileKey">
                <div class="select-core-wrapper">
                  <input ref="uploadInputRef" accept=".zip" type="file" style="display: none" @change="onFileChange" />

                  <t-button
                    v-if="!isUploading && !formData.packageFileKey"
                    variant="outline"
                    @click="triggerFileSelect"
                  >
                    <template #icon><t-icon name="upload" /></template>
                    点击选择 Zip 文件并上传
                  </t-button>

                  <div v-if="isUploading" class="uploading-state">
                    <div class="core-filename">正在上传: {{ uploadedFileName }} ({{ uploadedFileSize }})</div>
                    <t-progress theme="line" :percentage="uploadProgress" />
                    <div class="tip">别着急，喝杯咖啡☕️...</div>
                  </div>

                  <div v-if="!isUploading && isCheckingPackage" class="uploading-state">
                    <t-loading text="正在分析压缩包结构..." size="small" />
                  </div>

                  <div v-if="formData.packageFileKey && !isUploading && !isCheckingPackage" class="selected-core-card">
                    <div class="core-icon"><t-icon name="folder-zip" /></div>
                    <div class="core-info">
                      <div class="core-filename">{{ uploadedFileName }}</div>
                      <div class="core-url">
                        {{
                          detectedJars.length > 0
                            ? `发现 ${detectedJars.length} 个服务端核心文件`
                            : '未发现服务端核心文件'
                        }}
                        {{ detectedRoot ? `| 根目录: /${detectedRoot}` : '' }}
                      </div>
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

            <div v-show="currentStep === 2" class="step-content">
              <div v-if="detectedJars.length > 0">
                <t-alert theme="success" class="mb-4">
                  <template #message
                    >我们在压缩包中发现了以下服务端核心文件，请选择哪一个作为<b>启动核心</b>。</template
                  >
                </t-alert>

                <t-form-item label="选择启动核心" name="core">
                  <t-radio-group v-model="formData.core" class="vertical-radio-group">
                    <div v-for="jar in detectedJars" :key="jar" class="jar-option-item">
                      <t-radio :value="jar">{{ jar }}</t-radio>
                    </div>
                  </t-radio-group>
                </t-form-item>
              </div>

              <div v-else>
                <t-alert theme="warning" class="mb-4">
                  <template #message>在上传的包中未发现服务端核心文件。请在此处下载一个服务端核心。</template>
                </t-alert>

                <t-form-item label="补充服务端核心">
                  <t-radio-group v-model="downloadType" variant="default-filled">
                    <t-radio-button value="online">在线下载核心</t-radio-button>
                    <t-radio-button disabled value="manual">自行上传(不支持)</t-radio-button>
                  </t-radio-group>
                </t-form-item>

                <div v-if="downloadType === 'online'" class="online-select-area">
                  <t-form-item label="选择服务端核心" name="coreUrl">
                    <div class="select-core-wrapper">
                      <t-button variant="outline" @click="showCoreSelector = true">
                        <template #icon><t-icon name="cloud-download" /></template>
                        打开核心库
                      </t-button>
                      <div v-if="formData.core && downloadType === 'online'" class="selected-core-card">
                        <div class="core-icon"><t-icon name="check-circle-filled" /></div>
                        <div class="core-info">
                          <div class="core-filename">{{ formData.core }}</div>
                          <div class="core-url">将在创建时自动下载</div>
                        </div>
                      </div>
                    </div>
                  </t-form-item>
                </div>

                <div v-if="downloadType === 'manual'" class="online-select-area">
                  <div class="tip-text">请在整合包解压后手动放入核心，或在此处不填写等待创建后手动上传。</div>
                  <t-alert theme="error" message="此模式下建议确保压缩包内包含核心，或者使用在线下载功能。" />
                </div>
              </div>
            </div>

            <div v-show="currentStep === 3" class="step-content">
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
                    <t-radio-button value="local">本机 Java</t-radio-button>
                    <t-radio-button value="env">环境变量</t-radio-button>
                    <t-radio-button value="custom">自定义路径</t-radio-button>
                  </t-radio-group>

                  <div class="java-option-panel">
                    <div v-if="javaType === 'online'" class="flex-row">
                      <t-select v-model="selectedJavaVersion" :options="javaVersions" placeholder="请选择 Java 版本" />
                    </div>
                    <div v-if="javaType === 'local'" class="flex-row">
                      <t-select v-model="customJavaPath" :options="localJavaVersions" placeholder="请选择 Java" />
                      <t-button variant="text" @click="fetchJavaVersions(true)">刷新</t-button>
                    </div>
                    <div v-if="javaType === 'env'">
                      <t-input model-value="java" readonly disabled />
                    </div>
                    <div v-if="javaType === 'custom'">
                      <t-input v-model="customJavaPath" placeholder="C:\Path\To\java.exe" />
                    </div>
                  </div>
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 4" class="step-content">
              <t-row :gutter="[16, 24]">
                <t-col :xs="12" :span="6">
                  <t-form-item label="最小内存" name="minM">
                    <t-space :size="8" style="width: 100%">
                      <t-input-number
                        v-model="minMComputed"
                        :min="0"
                        :decimal-places="minUnit === 'GB' ? 1 : 0"
                        placeholder="Xms"
                        theme="column"
                        style="width: 100%"
                      />
                      <t-select
                        v-model="minUnit"
                        :options="unitOptions"
                        :clearable="false"
                        style="width: 80px"
                      />
                    </t-space>
                  </t-form-item>
                </t-col>

                <t-col :xs="12" :span="6">
                  <t-form-item label="最大内存" name="maxM">
                    <t-space :size="8" style="width: 100%">
                      <t-input-number
                        v-model="maxMComputed"
                        :min="0"
                        :decimal-places="maxUnit === 'GB' ? 1 : 0"
                        placeholder="Xmx"
                        theme="column"
                        style="width: 100%"
                      />
                      <t-select
                        v-model="maxUnit"
                        :options="unitOptions"
                        :clearable="false"
                        style="width: 80px"
                      />
                    </t-space>
                  </t-form-item>
                </t-col>
              </t-row>

              <t-form-item label="JVM 参数" name="args" style="margin-top: 16px">
                <t-textarea v-model="formData.args" placeholder="-XX:+UseG1GC" />
              </t-form-item>
            </div>

            <t-form-item class="step-actions" style="margin-top: 16px">
              <t-button v-if="currentStep > 0" theme="default" @click="prevStep">上一步</t-button>
              <t-button
                v-if="currentStep < 4"
                type="button"
                :loading="isUploading || isCheckingPackage"
                @click="nextStep"
                >下一步</t-button
              >
              <t-button v-if="currentStep === 4" theme="primary" type="submit" :loading="isSubmitting"
                >提交创建</t-button
              >
            </t-form-item>
          </t-form>
        </div>

        <div v-if="isCreating" class="step-content creation-progress">
          <div class="progress-title">正在创建整合包实例 ({{ createdServerId }})</div>
          <p>正在解压文件并配置环境...</p>
          <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(2)}%`" />
          <div ref="logContainerRef" class="log-container">
            <t-list :split="true">
              <t-list-item v-for="(log, index) in statusMessages" :key="index">
                <t-list-item-meta :description="`[${log.time}] ${log.message}`" />
              </t-list-item>
            </t-list>
          </div>
        </div>

        <div v-if="isSuccess" class="result-success">
          <t-icon class="result-success-icon" name="check-circle" />
          <div class="result-success-title">整合包服务器已部署成功</div>
          <div class="result-success-describe">文件已解压，环境已配置就绪</div>
          <div>
            <t-button @click="changeUrl('/instance/list')"> 返回服务端列表 </t-button>
            <t-button theme="default" @click="changeUrl(`/instance/console/${createdServerId}`)"> 前往控制台 </t-button>
          </div>
        </div>
      </div>
    </div>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />
  </t-card>
</template>

<style scoped lang="less">
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
  max-width: 600px;
  margin: 0;
  padding: 16px 0;
}
.mb-4 {
  margin-bottom: 16px;
}

.java-option-panel,
.online-select-area {
  margin-top: 16px;
  padding: 16px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);
}
.flex-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}

.select-core-wrapper {
  width: 100%;
}
.uploading-state {
  margin-top: 12px;
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
    }
    .core-url {
      font-size: 12px;
      color: var(--td-text-color-secondary);
    }
  }
}

.vertical-radio-group {
  display: block;
  .jar-option-item {
    margin-bottom: 8px;
    &:last-child {
      margin-bottom: 0;
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
    font-weight: 500;
  }
  &-describe {
    margin: 8px 0 32px;
    font-size: 14px;
  }
}

/* --- 移动端适配 --- */
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
