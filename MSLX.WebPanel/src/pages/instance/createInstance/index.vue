<script setup lang="ts">
import { onUnmounted, ref } from 'vue';
import { request } from '@/utils/request'; // 使用你提供的 request 工具
import { useRouter } from 'vue-router';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

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

const formData = ref({
  name: '',
  path: '',
  java: 'java',
  core: '',
  coreUrl: '',
  coreSha256: '',
  minM: 1024,
  maxM: 4096,
  args: '',
});

// 表单校验规则

const FORM_RULES: FormRules = {
  name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
  java: [{ required: true, message: 'Java 路径不能为空', trigger: 'blur' }],
  core: [{ required: true, message: '核心名称不能为空', trigger: 'blur' }],
  coreUrl: [
    {
      pattern: /^https?:\/\/.+/,
      message: '下载地址必须以 http:// 或 https:// 开头',
      trigger: 'blur',
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
  const fieldsToValidate = new Set(stepValidationFields[currentStep.value]);

  const validateResult = await formRef.value.validate();

  if (validateResult === true) {
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
    return;
  }

  const hasErrorInCurrentStep = Object.keys(validateResult).some((field) => fieldsToValidate.has(field));

  if (hasErrorInCurrentStep) {
    MessagePlugin.warning('请检查当前步骤的输入');
  } else {
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
  }
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

    createdServerId.value = serverId.toString(); // 存储创建的 ServerId

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
      // 主动标记成功
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
    // 不成功才弹出错误
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

// 成功页面相关导航
const goToHome = () => {
  isSuccess.value = false;
  currentStep.value = 0;
  formData.value = { ...formData.value, name: '', core: '', coreUrl: '',coreSha256: '', path: '', args: '' };
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
                  <p>你可以输入 'java' 来使用系统默认环境，或提供 java.exe 的完整路径。</p>
                </template>
              </t-alert>
              <t-form-item label="Java 路径" name="java">
                <t-input v-model="formData.java" placeholder="例如: C:\Program Files\Java\jdk-17\bin\java.exe" />
              </t-form-item>
            </div>

            <div v-show="currentStep === 2" class="step-content">
              <t-form-item label="核心名称" name="core" help="这是下载后保存的文件名，例如 paper.jar">
                <t-input v-model="formData.core" placeholder="例如: paper-1.18.2.jar" />
              </t-form-item>
              <t-form-item label="核心下载地址 (可选)" name="coreUrl" help="如果留空，将不会自动下载核心文件">
                <t-input v-model="formData.coreUrl" placeholder="http://.../server.jar" />
              </t-form-item>
              <t-form-item label="核心下载文件Sha256" name="coreSha256" help="如果留空，将不会校验下载文件的完整性">
                <t-input v-model="formData.coreSha256" placeholder="" />
              </t-form-item>
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
  </t-card>
</template>

<style scoped lang="less">
/* --- 新增：2 列表单布局 --- */
.main-layout-container {
  display: flex;
  gap: 32px;
}

.steps-aside {
  flex-shrink: 0;
  width: 220px; // 步骤条的宽度
  border-right: 1px solid var(--td-border-level-2-color);
  padding-right: 32px;
}

.main-content {
  flex-grow: 1;
  min-width: 0; // 防止 flex 溢出
}

/* --- 修改：调整原样式以适应新布局 --- */

// form-step-container
.form-step-container {
  padding-top: 0; // 原为 24px，现在由布局控制
}

// step-content
.step-content {
  max-width: 600px;
  margin: 0; // 原为 24px auto，移除自动居中
  padding: 16px 0; // 保留垂直 padding
}

// java-alert
.java-alert {
  margin-bottom: 24px;
}

// step-actions
.step-actions {
  margin-top: 32px;

  .t-button {
    margin-right: 16px;
  }
}

// creation-progress
.creation-progress {
  text-align: center;
  padding: 16px; // 保持内边距

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

// --- result-success ---
.result-success {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  min-height: 50vh; // 原为 height: 75vh，改为 min-height 更灵活
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

/* --- 新增：响应式布局 (移动端) --- */
@media (max-width: 768px) {
  .main-layout-container {
    flex-direction: column; // 垂直堆叠
    gap: 24px;
  }

  .steps-aside {
    width: 100%;
    border-right: none; // 移除右边框
    padding-right: 0;
    border-bottom: 1px solid var(--td-border-level-2-color); // 添加下边框
    padding-bottom: 24px;
  }

  .step-content {
    max-width: 100%; // 移动端占满宽度
  }

  .result-success {
    min-height: 40vh;
  }
}
</style>
