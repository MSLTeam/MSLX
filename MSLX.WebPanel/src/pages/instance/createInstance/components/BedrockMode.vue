<script setup lang="ts">
import { onUnmounted, ref, onMounted, computed, nextTick, watch } from 'vue';
import { type FormRules, MessagePlugin } from 'tdesign-vue-next';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';
import { getHubUrl } from '@/utils/hub';

import { postCreateInstanceQuickMode } from '@/api/instance';
import { CreateInstanceQucikModeModel } from '@/api/model/instance';
import { changeUrl } from '@/router';
import { useInstanceListStore } from '@/store/modules/instance';
import { getServerCoreDownloadInfo, getServerCoreGameVersion } from '@/api/mslapi/serverCore';

// 模型扩展
interface BedrockCreateModel extends CreateInstanceQucikModeModel {
  packageUrl?: string;
  packageSha256?: string;
  ignoreEula?: boolean;
  deployMode: 'native' | 'docker';
  imageType: 'builtIn' | 'custom';
}

// 端口映射项接口
interface PortMapping {
  containerPort: string;
  hostPort: string;
  protocol: 'udp' | 'tcp';
}

// 状态管理
const userStore = useUserStore();
const formRef = ref(null);
const instanceListstore = useInstanceListStore();

const osType = computed(() => userStore.userInfo.systemInfo.osType?.toLowerCase() || '');
const isMac = computed(() => osType.value.includes('mac'));
const isWin = computed(() => osType.value.includes('window'));

const currentStep = ref(0);
const isSubmitting = ref(false);
const isCreating = ref(false);
const isSuccess = ref(false);
const progress = ref(0);
const statusMessages = ref<{ time: string; message: string; progress: number | null }[]>([]);
const hubConnection = ref<HubConnection | null>(null);
const createdServerId = ref<string | null>(null);
const logContainerRef = ref<HTMLDivElement | null>(null);

// 基岩版版本选择状态
const availableVersions = ref<{ label: string; value: string }[]>([]);
const selectedVersion = ref('');
const isFetchingVersions = ref(false);

// 动态端口映射状态列表
const portMappings = ref<PortMapping[]>([{ containerPort: '19132', hostPort: '19132', protocol: 'udp' }]);

const formData = ref<BedrockCreateModel>({
  name: '新建基岩版服务器',
  path: '',
  java: 'none',
  core: 'none',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
  packageFileKey: '',
  packageLocalPath: '',
  packageUrl: '',
  packageSha256: '',
  minM: 1027,
  maxM: 1027,
  args: '',
  ignoreEula: true,
  deployMode: 'native', // native 或 docker
  imageType: 'builtIn', // builtIn 或 custom
  dockerImage: 'MSLX://DockerImage/Java/25',
  dockerPorts: '',
});

// 添加动态端口方法
const addPortMapping = () => {
  portMappings.value.push({ containerPort: '', hostPort: '', protocol: 'udp' });
};

// 删除动态端口方法
const removePortMapping = (index: number) => {
  portMappings.value.splice(index, 1);
};

// 获取版本列表
const fetchVersions = async () => {
  isFetchingVersions.value = true;
  try {
    const res = await getServerCoreGameVersion('bedrock-server');
    const allVersions = res.versions || [];

    let filteredVersions = [];
    // docker / mac
    if (formData.value.deployMode === 'docker' || isMac.value) {
      filteredVersions = allVersions.filter((v) => v.includes('linux-'));
    } else if (isWin.value) {
      filteredVersions = allVersions.filter((v) => v.includes('win-'));
    } else {
      filteredVersions = allVersions.filter((v) => v.includes('linux-'));
    }

    availableVersions.value = filteredVersions.map((v) => ({ label: v, value: v }));

    // 如果当前选中的版本不在新列表里，默认选第一个
    if (availableVersions.value.length > 0) {
      if (!availableVersions.value.some((v) => v.value === selectedVersion.value)) {
        selectedVersion.value = availableVersions.value[0].value;
      }
    } else {
      selectedVersion.value = '';
    }
  } catch (e: any) {
    MessagePlugin.warning('获取基岩版版本列表失败: ' + e.message);
  } finally {
    isFetchingVersions.value = false;
  }
};

// 监听部署模式变化
watch(
  () => formData.value.deployMode,
  (newMode) => {
    if (newMode === 'docker') {
      formData.value.args = './bedrock_server';
    } else {
      formData.value.args = isWin.value ? 'bedrock_server.exe' : './bedrock_server';
    }
    fetchVersions();
  },
);

// 监听镜像选项变化
watch(
  () => formData.value.imageType,
  (newType) => {
    if (newType === 'builtIn') {
      formData.value.dockerImage = 'MSLX://DockerImage/Java/25';
    } else {
      formData.value.dockerImage = '';
    }
  },
);

onMounted(() => {
  // 如果是 Mac，强制锁定为 Docker 部署
  if (isMac.value) {
    formData.value.deployMode = 'docker';
    formData.value.args = './bedrock_server';
  } else {
    formData.value.args = isWin.value ? 'bedrock_server.exe' : './bedrock_server';
  }
  fetchVersions();
});

// 表单校验规则
const FORM_RULES = computed<FormRules>(() => {
  const rules: FormRules = {
    name: [{ required: true, message: '实例名称不能为空', trigger: 'blur' }],
    args: [{ required: true, message: '请输入启动指令', trigger: 'blur' }],
  };
  if (formData.value.deployMode === 'docker') {
    rules.dockerImage = [{ required: true, message: '镜像地址不能为空', trigger: 'blur' }];
  }
  return rules;
});

const stepValidationFields = [['name', 'path'], ['dockerImage'], ['args'], []];

// 步骤导航
const prevStep = () => {
  if (currentStep.value > 0) {
    currentStep.value -= 1;
  }
};

const nextStep = async () => {
  // 第二步：选择版本与部署模式
  if (currentStep.value === 1) {
    if (!selectedVersion.value) {
      MessagePlugin.warning('请选择一个基岩版服务端版本');
      return;
    }

    // 获取下载链接和Sha256
    try {
      isSubmitting.value = true;
      const downloadRes = await getServerCoreDownloadInfo('bedrock-server', selectedVersion.value);
      formData.value.packageUrl = downloadRes.url;
      formData.value.packageSha256 = downloadRes.sha256;
      isSubmitting.value = false;
    } catch (e: any) {
      isSubmitting.value = false;
      MessagePlugin.error('获取版本下载信息失败: ' + e.message);
      return;
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
    if (currentStep.value < 3) {
      currentStep.value += 1;
    }
  }
};

// 提交 & SignalR 状态
const onSubmit = async () => {
  const validateResult = await formRef.value.validate();
  if (validateResult !== true) {
    MessagePlugin.warning('请检查表单所有内容');
    return;
  }

  // 处理端口映射数据结构转换
  if (formData.value.deployMode === 'docker') {
    const validPorts = portMappings.value
      .filter((p) => p.containerPort.trim() && p.hostPort.trim())
      .map((p) => `${p.hostPort.trim()}:${p.containerPort.trim()}/${p.protocol}`);

    formData.value.dockerPorts = validPorts.join(',');
  } else {
    formData.value.dockerPorts = '';
    formData.value.dockerImage = '';
  }

  isSubmitting.value = true;
  statusMessages.value = [];

  // 组装最终提交给后端的 API 数据
  const apiData = {
    ...formData.value,
    path: formData.value.path || null,
    java: formData.value.deployMode === 'docker' ? 'docker-custom' : formData.value.java,
    dockerImage: formData.value.deployMode === 'docker' ? formData.value.dockerImage : undefined,
    dockerPorts: formData.value.deployMode === 'docker' ? formData.value.dockerPorts : undefined,
  };

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
    const errorMessage = error.message || '创建请求失败，请检查网络或后端 service';
    MessagePlugin.error(errorMessage);
    isSubmitting.value = false;
  }
};

// SignalR 链接
const startSignalRConnection = async (serverId: string) => {
  let isSuccessHandled = false;
  const hubUrlStr = getHubUrl('/api/hubs/creationProgressHub');

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrlStr, { withCredentials: false })
    .configureLogging(LogLevel.Information)
    .build();

  const addLog = (message: string, progress: number | null = null) => {
    statusMessages.value.push({
      time: new Date().toLocaleTimeString(),
      message,
      progress,
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
      MessagePlugin.success('服务器创建成功！');
      hubConnection.value?.stop();
      isCreating.value = false;
      isSuccess.value = true;
      currentStep.value = 5;
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
});

const goToHome = () => {
  isSuccess.value = false;
  currentStep.value = 0;
  portMappings.value = [
    { containerPort: '19132', hostPort: '19132' ,protocol:'udp' },
  ];
  formData.value = {
    ...formData.value,
    name: '新建基岩版服务器',
    path: '',
    deployMode: isMac.value ? 'docker' : 'native',
    java: isMac.value ? 'docker-custom' : 'none',
    imageType: 'builtIn',
    dockerImage: 'MSLX://DockerImage/Java/25',
    args:
      isMac.value || formData.value.deployMode === 'docker'
        ? './bedrock_server'
        : isWin.value
          ? 'bedrock_server.exe'
          : './bedrock_server',
    packageUrl: '',
    packageSha256: '',
  };
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
          <t-step-item title="基本信息" content="填写实例名称和路径" />
          <t-step-item title="服务端版本" content="选择官方基岩版核心" />
          <t-step-item title="启动配置" content="设置启动指令与参数" />
          <t-step-item title="确认信息" content="核对并提交" />
          <t-step-item title="部署实例" content="提交并等待创建" />
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
              <t-form-item label="实例名称" name="name">
                <t-input v-model="formData.name" placeholder="为你的服务器起个名字" class="!w-full sm:!w-[28rem]" />
              </t-form-item>

              <t-form-item
                label="实例路径 (可选)"
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
                  placeholder="例如: D:\BedrockServer"
                  class="!w-full sm:!w-[28rem] !font-mono"
                />
              </t-form-item>

              <t-form-item label="忽略 EULA 提示 (基岩版请保持本功能开启)" name="ignoreEula">
                <div class="flex items-center gap-3">
                  <t-switch v-model="formData.ignoreEula" size="large" />
                  <span
                    class="text-sm font-bold transition-colors"
                    :class="
                      formData.ignoreEula ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-secondary)]'
                    "
                  >
                    {{ formData.ignoreEula ? '已开启' : '已关闭' }}
                  </span>
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 1" class="list-item-anim flex-1 pt-1">
              <t-alert theme="info" title="基岩版服务端说明" class="!mb-6 !rounded-xl">
                <template #message>
                  <div class="text-[12px] mt-1 text-zinc-600 dark:text-zinc-400">
                    MSLX 将为您自动拉取官方 Bedrock Server，并根据您的系统架构提供对应的可用版本（当前系统:
                    {{ osType }}）。
                  </div>
                </template>
              </t-alert>

              <t-form-item label="部署模式" name="deployMode">
                <t-radio-group v-model="formData.deployMode" variant="default-filled">
                  <t-radio-button value="native" :disabled="isMac">本机原生部署</t-radio-button>
                  <t-radio-button value="docker">Docker 容器部署</t-radio-button>
                </t-radio-group>
                <div v-if="isMac" class="text-xs text-[var(--color-warning)] mt-1">
                  提示：当前为 macOS 环境，由于基岩版本身不提供 Mac 核心，系统已自动为您锁定为 Docker 虚拟化部署模式。
                </div>
              </t-form-item>

              <template v-if="formData.deployMode === 'docker'">
                <t-form-item label="Docker 镜像选项" name="imageType">
                  <t-radio-group v-model="formData.imageType" variant="default-filled">
                    <t-radio-button value="builtIn">内置推荐镜像</t-radio-button>
                    <t-radio-button value="custom">自定义外部镜像</t-radio-button>
                  </t-radio-group>
                </t-form-item>

                <t-form-item label="Docker 镜像地址" name="dockerImage">
                  <t-input
                    v-model="formData.dockerImage"
                    :disabled="formData.imageType === 'builtIn'"
                    placeholder="请输入 Docker 镜像名，如 ubuntu:latest"
                    class="!w-full sm:!w-[28rem]"
                  />
                </t-form-item>
              </template>

              <t-form-item label="选择服务端版本" name="selectedVersion">
                <div class="w-full sm:w-[28rem]">
                  <t-select
                    v-model="selectedVersion"
                    :options="availableVersions"
                    :loading="isFetchingVersions"
                    placeholder="请选择基岩版版本"
                    class="!w-full"
                    filterable
                  />
                </div>
              </t-form-item>
            </div>

            <div v-show="currentStep === 2" class="list-item-anim flex-1 pt-1">
              <t-form-item label="启动指令" name="args" class="w-full sm:!w-[40rem]">
                <template #help>
                  <span class="text-[11px] text-zinc-500 mt-1 inline-block">
                    已为您自动匹配启动指令，非必要请勿修改哦～
                  </span>
                </template>
                <t-textarea
                  v-model="formData.args"
                  placeholder="例如: bedrock_server.exe 或 ./bedrock_server"
                  :autosize="{ minRows: 3, maxRows: 6 }"
                  class="code-font-textarea !bg-zinc-50/50 dark:!bg-zinc-900/30 !rounded-xl !font-mono"
                />
              </t-form-item>

              <template v-if="formData.deployMode === 'docker'">
                <t-form-item label="Docker 端口映射配置">
                  <div
                    class="w-full sm:w-[40rem] bg-zinc-50/50 dark:bg-zinc-900/20 p-4 rounded-2xl border border-zinc-200/60 dark:border-zinc-800"
                  >
                    <div class="text-xs text-zinc-400 mb-3">
                      为容器内的服务端映射宿主机访问端口。基岩版默认端口通常为 19132
                    </div>

                    <div
                      v-for="(item, index) in portMappings"
                      :key="index"
                      class="flex items-center gap-3 mb-3 list-item-anim"
                    >
                      <t-input v-model="item.hostPort" placeholder="宿主机端口" class="flex-1" />
                      <span class="text-zinc-400 font-bold">:</span>
                      <t-input v-model="item.containerPort" placeholder="容器端口" class="flex-1" />

                      <t-select v-model="item.protocol" style="width: 90px">
                        <t-option label="UDP" value="udp" />
                        <t-option label="TCP" value="tcp" />
                      </t-select>

                      <t-button theme="danger" variant="text" shape="circle" @click="removePortMapping(index)">
                        <t-icon name="delete" />
                      </t-button>
                    </div>

                    <t-button theme="primary" variant="dashed" class="w-full !mt-2" @click="addPortMapping">
                      <template #icon>
                        <t-icon name="add" />
                      </template>
                      添加端口映射规则
                    </t-button>
                  </div>
                </t-form-item>
              </template>
            </div>

            <div v-show="currentStep === 3" class="list-item-anim flex-1 pt-1">
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
                    >服务端核心</span
                  >
                  <div class="flex flex-col sm:items-end text-left sm:text-right">
                    <div class="flex items-center gap-2">
                      <span
                        class="text-sm font-bold text-[var(--td-text-color-primary)] truncate max-w-[200px] sm:max-w-[300px]"
                        >基岩版 {{ selectedVersion }}</span
                      >
                      <t-tag theme="primary" variant="light" size="small" class="!rounded">在线下载</t-tag>
                    </div>
                  </div>
                </div>

                <div
                  v-if="formData.deployMode === 'docker'"
                  class="flex flex-col sm:flex-row sm:items-center justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold shrink-0">Docker 镜像</span>
                  <span class="text-sm font-mono text-[var(--td-text-color-primary)] truncate max-w-[300px]">{{
                    formData.dockerImage
                  }}</span>
                </div>

                <div
                  v-if="formData.deployMode === 'docker'"
                  class="flex flex-col sm:flex-row sm:items-start justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold shrink-0 mt-0.5"
                    >运行端口映射</span
                  >
                  <div class="flex flex-wrap gap-1.5 justify-end max-w-md">
                    <t-tag
                      v-for="(p, idx) in portMappings.filter((x) => x.hostPort)"
                      :key="idx"
                      size="small"
                      variant="outline"
                      theme="warning"
                    >
                      {{ p.hostPort }} ➜ {{ p.containerPort }} ({{p.protocol.toUpperCase()}})
                    </t-tag>
                  </div>
                </div>

                <div
                  class="flex flex-col sm:flex-row sm:items-start justify-between py-4 border-b border-dashed border-zinc-200 dark:border-zinc-800/80"
                >
                  <span class="text-sm text-[var(--td-text-color-secondary)] font-bold mb-2 sm:mb-0 shrink-0 mt-1"
                    >启动指令</span
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
                  <strong class="text-[var(--color-primary)] mx-1">提交创建</strong
                  >，系统将自动开始下载资源并部署基岩版实例。</template
                >
              </t-alert>
            </div>

            <div class="mt-auto pt-6 border-t border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
              <t-button v-if="currentStep > 0 && currentStep < 4" theme="default" @click="prevStep">上一步</t-button>
              <div v-else></div>
              <t-button v-if="currentStep < 3" theme="primary" type="button" :loading="isSubmitting" @click="nextStep"
                >下一步</t-button
              >
              <t-button v-if="currentStep === 3" theme="primary" type="submit" :loading="isSubmitting"
                >提交创建</t-button
              >
            </div>
          </t-form>
        </div>

        <div v-if="isCreating" class="h-full flex flex-col items-center justify-center py-8 list-item-anim">
          <div class="text-lg font-bold text-[var(--td-text-color-primary)] mb-2 tracking-tight">
            正在创建实例 ({{ createdServerId }})
          </div>
          <p class="text-sm text-[var(--td-text-color-secondary)] mb-6">
            请勿关闭此页面，下载与创建过程可能需要几分钟...
          </p>

          <div class="w-full max-w-lg !my-6">
            <t-progress theme="plump" :percentage="progress" :label="`${progress.toFixed(2)}%`" />
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
            基岩版服务器 ({{ createdServerId }}) 已创建成功
          </div>

          <div class="text-sm text-[var(--td-text-color-secondary)] leading-[22px] !my-2 !mb-8">
            你现在可以去服务器列表启动它了
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

/* 等宽字体与代码 */
:deep(.code-font-textarea textarea) {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre;
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
