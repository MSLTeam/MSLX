<script setup lang="ts">
import { onUnmounted, ref, watch, onMounted } from 'vue';
import { request } from '@/utils/request';
import { useRouter } from 'vue-router';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

import ServerCoreSelector from './components/ServerCoreSelector.vue';
import { getJavaVersionList } from '@/api/mslapi/java';

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

// 核心选择相关状态
const downloadType = ref('online'); // 'online' | 'manual'
const showCoreSelector = ref(false);

// Java 选择相关状态
const javaType = ref('online');
const javaVersions = ref<{ label: string; value: string }[]>([]);
const selectedJavaVersion = ref('');
const customJavaPath = ref('');

const fetchJavaVersions = async () => {
  try {
    const res = await getJavaVersionList(userStore.userInfo.systemInfo.osType.toLowerCase().replace('os',''),userStore.userInfo.systemInfo.osArchitecture.toLowerCase());
    if (res && Array.isArray(res)) {
      javaVersions.value = res.map(v => ({ label: `Java ${v}`, value: v }));
      if (javaVersions.value.length > 0 && !selectedJavaVersion.value) {
        selectedJavaVersion.value = javaVersions.value[1].value; // 默认java21
      }
    }
  } catch (e) {
    MessagePlugin.warning('获取在线Java版本失败' + e.message);
  }
};

onMounted(() => {
  fetchJavaVersions();
});

const formData = ref({
  name: '',
  path: '',
  java: '',
  core: '',
  coreUrl: '',
  coreSha256: '',
  minM: 1024,
  maxM: 4096,
  args: '',
});

// 监听选择java的状态变量 修改表单数据
watch([javaType, selectedJavaVersion, customJavaPath], ([type, ver, path]) => {
  if (type === 'env') {
    formData.value.java = 'java';
  } else if (type === 'custom') {
    formData.value.java = path;
  } else if (type === 'online') {
    formData.value.java = ver ? `MSLX://Java/${ver}` : '';
  }

  if (formData.value.java) {
    formRef.value?.validate({ fields: ['java'] });
  }
}, { immediate: true });

// 表单校验规则
const FORM_RULES: FormRules = {
  name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
  java: [{ required: true, message: '请配置 Java 环境', trigger: 'change' }],
  core: [{ required: true, message: '必须指定核心名称', trigger: 'change' }],
  coreUrl: [
    {
      validator: (val) => {
        if (downloadType.value === 'online' && !val) return { result: false, message: '请选择一个服务端核心', type: 'error' };
        if (val && !/^https?:\/\/.+/.test(val)) return { result: false, message: '下载地址必须以 http(s) 开头', type: 'error' };
        return true;
      },
      trigger: 'change'
    },
  ],
  minM: [{ required: true, min: 1, message: '最小内存必须大于0', trigger: 'blur' }],
  maxM: [{ required: true, min: 1, message: '最大内存必须大于0', trigger: 'blur' }],
};

const stepValidationFields = [['name', 'path'], ['java'], ['core', 'coreUrl','coreSha256'], ['minM', 'maxM', 'args']];

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
    } else {
      MessagePlugin.warning('自行上传功能暂未开放，请使用在线下载');
      return;
    }
  }

  const fieldsToValidate = new Set(stepValidationFields[currentStep.value]);
  const validateResult = await formRef.value.validate();

  if (validateResult === true) {
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
    return;
  }

  // 检查当前步骤是否有错误
  const hasErrorInCurrentStep = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasErrorInCurrentStep) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
  }
};

// 处理核心选择组件回调
const onCoreSelected = (data: { core: string; version: string; url: string; sha256: string; filename: string }) => {
  formData.value.core = data.filename;
  formData.value.coreUrl = data.url;
  formData.value.coreSha256 = data.sha256;
  MessagePlugin.success(`已选择: ${data.core} (${data.version})`);

  formRef.value.validate({ fields: ['core', 'coreUrl'] });
};

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
    args: formData.value.args || null,
  };

  try {
    const response = await request.post({
      url: '/api/instance/createServer',
      data: apiData,
    });

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
    name: '', core: '', coreUrl: '', coreSha256: '', path: '', args: ''
  };
  downloadType.value = 'online';
  javaType.value = 'online';
  customJavaPath.value = '';
};

const viewDetails = () => {
  router.push('/detail/advanced');
};
</script>

<template>
  <t-card>
    <div class="main-layout-container">
      <div class="steps-aside">
        <t-steps layout="vertical" style="margin-top: 16px;" :current="currentStep" status="process" readonly>
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
          <t-form ref="formRef" :data="formData" :rules="FORM_RULES" label-align="top" class="step-form" @submit="onSubmit">

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
                    <li>MC 1.17 及以上: 需要 Java 17 或更高版本。</li>
                    <li>MC 1.13 - 1.16: 需要 Java 11。</li>
                    <li>MC 1.12 及以下: 需要 Java 8。</li>
                  </ul>
                </template>
              </t-alert>

              <t-form-item label="Java 来源" name="java">
                <div style="width: 100%">
                  <t-radio-group v-model="javaType" variant="default-filled">
                    <t-radio-button value="online">在线下载</t-radio-button>
                    <t-radio-button value="env">环境变量</t-radio-button>
                    <t-radio-button value="custom">自定义路径</t-radio-button>
                  </t-radio-group>

                  <div class="java-option-panel">
                    <div v-if="javaType === 'online'" class="flex-row">
                      <t-select v-model="selectedJavaVersion" :options="javaVersions" placeholder="请选择 Java 版本" />
                      <div class="tip">将下载并使用 Java {{ selectedJavaVersion || '?' }} {{ userStore.userInfo.systemInfo.osType.toLowerCase().replace('os','')}} / {{userStore.userInfo.systemInfo.osArchitecture.toLowerCase()}}</div>
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
                  <t-radio-button value="manual" disabled>自行上传 (暂不可用)</t-radio-button>
                </t-radio-group>
              </t-form-item>

              <div v-if="downloadType === 'online'" class="online-select-area">
                <t-form-item label="选择服务端核心" name="coreUrl"> <div class="select-core-wrapper">
                  <t-button variant="outline" @click="showCoreSelector = true">
                    <template #icon><t-icon name="cloud-download" /></template>
                    点击打开服务端核心选择库
                  </t-button>

                  <div v-if="formData.core" class="selected-core-card">
                    <div class="core-icon"><t-icon name="check-circle-filled" /></div>
                    <div class="core-info">
                      <div class="core-filename">{{ formData.core }}</div>
                      <div class="core-url" title="MSLX 将在稍后帮您自动下载此文件...">MSLX 将在稍后帮您自动下载此文件...</div>
                    </div>
                    <t-button shape="circle" variant="text" theme="danger" @click="formData.core = ''; formData.coreUrl = '';">
                      <t-icon name="close" />
                    </t-button>
                  </div>
                </div>
                </t-form-item>
                <input v-model="formData.coreSha256" type="hidden" />
              </div>

              <div v-if="downloadType === 'manual'">
                <t-alert theme="warning" message="该功能尚未实现，请选择在线下载。" />
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
              <t-button v-if="currentStep === 3" theme="primary" type="submit" :loading="isSubmitting"> 提交创建 </t-button>
            </t-form-item>
          </t-form>
        </div>

        <div v-if="isCreating" class="step-content creation-progress">
          <div class="progress-title">正在创建实例 ({{ createdServerId }})</div>
          <p>请勿关闭此页面，创建过程可能需要几分钟...</p>

          <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(2)}%`" />

          <div class="log-container">
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

    <server-core-selector
      v-model:visible="showCoreSelector"
      @confirm="onCoreSelected"
    />
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
  max-width: 600px;
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
