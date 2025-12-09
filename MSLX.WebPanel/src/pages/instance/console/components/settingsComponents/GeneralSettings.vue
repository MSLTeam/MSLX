<script setup lang="ts">
import { ref, onMounted, watch, nextTick, onUnmounted } from 'vue';
import { useRoute } from 'vue-router';
import {
  MessagePlugin,
  type FormRules,
  type FormInstanceFunctions,
  DialogPlugin,
  NotifyPlugin,
} from 'tdesign-vue-next';
import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
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

const instanceId = parseInt(route.params.id as string);

const formRef = ref<FormInstanceFunctions | null>(null);
const loading = ref(false);
const submitting = ref(false);

// 路径编辑状态
const isPathEditable = ref(false);

// 编码选项
const encodingOptions = [
  { label: 'UTF-8', value: 'utf-8' },
  { label: 'GBK', value: 'gbk' }
];

// 数据模型
const formData = ref<UpdateInstanceModel>({
  id: instanceId,
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
  autoRestart: false,
  runOnStartup: false,
  inputEncoding: 'utf-8',
  outputEncoding: 'utf-8',
  coreUrl: '',
  coreSha256: '',
  coreFileKey: '',
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

watch(
  [javaType, selectedJavaVersion, customJavaPath],
  ([type, ver, path]) => {
    if (type === 'env') formData.value.java = 'java';
    else if (type === 'custom' || type === 'local') formData.value.java = path;
    else if (type === 'online') formData.value.java = ver ? `MSLX://Java/${ver}` : '';
  }
);

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
const authSelectType = ref(''); // none, msl, littleskin, custom
const customAuthUrl = ref('');
const authOptions = [
  { label: '官方/离线模式 (无)', value: 'none' },
  { label: 'MSL 统一身份验证', value: 'https://skin.mslmc.net/api/yggdrasil' },
  { label: 'LittleSkin', value: 'https://littleskin.cn/api/yggdrasil' },
  { label: '自定义服务器', value: 'custom' }
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
    try { await deleteUpload(formData.value.coreFileKey); } catch {}
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
      duration: 5000
    });
  }
  isPathEditable.value = !isPathEditable.value;
};

// --- 提交与 SignalR ---
const showProgressDialog = ref(false);
const progressPercent = ref(0);
const progressLogs = ref<{ time: string; msg: string }[]>([]);
const hubConnection = ref<HubConnection | null>(null);

const rules: FormRules = {
  name: [{ required: true, message: '服务器名称不能为空', trigger: 'blur' }],
  base: [{ required: true, message: '基础路径不能为空', trigger: 'blur' }],
  java: [{ required: true, message: 'Java 环境不能为空', trigger: 'change' }],
  core: [{ required: true, message: '核心文件名不能为空', trigger: 'change' }],
  minM: [{ required: true, message: '必填', trigger: 'blur' }],
  maxM: [{ required: true, message: '必填', trigger: 'blur' }],
};

const initData = async () => {
  loading.value = true;
  try {
    await fetchJavaVersions();
    const res = await getInstanceSettings(instanceId);
    formData.value = {
      ...formData.value,
      ...res,
      coreUrl: '',
      coreFileKey: '',
      coreSha256: ''
    };

    isPathEditable.value = false;

    // 解析 Java
    if (res.java === 'java') {
      javaType.value = 'env';
    } else if (res.java.startsWith('MSLX://Java/')) {
      javaType.value = 'online';
      selectedJavaVersion.value = res.java.replace('MSLX://Java/', '');
    } else {
      const inLocal = localJavaVersions.value.find(v => v.value === res.java);
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

onMounted(initData);

const onSubmit = async () => {
  const result = await formRef.value?.validate();
  if (result !== true) return;

  const isChangingCore = !!formData.value.coreUrl || !!formData.value.coreFileKey;
  if (isChangingCore) {
    const confirm = await new Promise((resolve) => {
      const dialog = DialogPlugin.confirm({
        header: '确认变更核心文件?',
        body: '检测到您上传或选择了新的核心文件，这将覆盖服务器现有的部署。',
        theme: 'warning',
        onConfirm: () => { dialog.hide(); resolve(true); },
        onClose: () => { dialog.hide(); resolve(false); },
      });
    });
    if (!confirm) return;
  }

  submitting.value = true;
  try {
    const res = await postInstanceSettings(formData.value);
    // 适配后端返回
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
  const hubUrl = new URL('/api/hubs/update', baseUrl);
  hubUrl.searchParams.append('x-api-key', token);

  hubConnection.value = new HubConnectionBuilder()
    .withUrl(hubUrl.toString(), { withCredentials: false })
    .configureLogging(LogLevel.None)
    .build();

  hubConnection.value.on('UpdateStatus', (msg: string, prog: number, isErr: boolean) => {
    progressLogs.value.push({ time: new Date().toLocaleTimeString(), msg: isErr ? `[错误] ${msg}` : msg });
    nextTick(() => {
      const div = document.getElementById('update-log-box');
      if(div) div.scrollTop = div.scrollHeight;
    });
    if (prog >= 0) progressPercent.value = prog;
    if (prog === 100) {
      MessagePlugin.success('更新完成');
      closeSignalR(true);
    } else if (isErr || prog === -1) {
    }
  });

  try {
    await hubConnection.value.start();
    await hubConnection.value.invoke('JoinGroup', instanceId.toString());
  } catch (e) {
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
  <div class="settings-container">
    <t-form
      ref="formRef"
      :data="formData"
      :rules="rules"
      label-width="0"
      @submit="onSubmit"
    >
      <div class="setting-group-title">基础设置</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">服务器名称</div>
          <div class="desc">在面板列表中显示的别名，支持中文</div>
        </div>
        <div class="setting-control">
          <t-input v-model="formData.name" placeholder="请输入名称" />
        </div>
      </div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">实例路径</div>
          <div class="desc">
            <span :class="{ 'text-warning': isPathEditable }">
                {{ isPathEditable ? '警告：修改路径可能导致无法找到原文件' : '服务器文件的物理存储路径，非必要请勿修改' }}
            </span>
          </div>
        </div>
        <div class="setting-control">
          <t-input v-model="formData.base" :disabled="!isPathEditable">
            <template #suffix>
              <t-tooltip :content="isPathEditable ? '点击锁定' : '点击解锁编辑 (慎重)'">
                <t-button variant="text" shape="square" @click="togglePathEdit">
                  <template #icon>
                    <lock-off-icon v-if="isPathEditable" style="color: var(--td-warning-color)" />
                    <lock-on-icon v-else />
                  </template>
                </t-button>
              </t-tooltip>
            </template>
          </t-input>
        </div>
      </div>

      <div class="setting-group-title">运行环境</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">Java 运行时</div>
          <div class="desc">Minecraft 运行所依赖的 Java 版本</div>
        </div>
        <div class="setting-control">
          <t-select v-model="javaType" :options="[
            { label: 'MSLX 在线下载', value: 'online' },
            { label: '使用本地版本', value: 'local' },
            { label: '自定义路径', value: 'custom' },
            { label: '环境变量', value: 'env' }
          ]" />

          <div style="margin-top: 8px">
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

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">启动参数 (JVM Args)</div>
          <div class="desc">传递给 Java 的启动参数，如 GC 策略 (例如 -XX:+UseG1GC)</div>
        </div>
        <div class="setting-control">
          <t-textarea
            v-model="formData.args"
            :autosize="{ minRows: 2, maxRows: 4 }"
            placeholder="无特殊需求请留空"
          />
        </div>
      </div>

      <div class="setting-group-title">核心管理</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">服务端核心文件</div>
          <div class="desc">
            指定启动的 Jar 文件名。如果文件已存在于目录中，直接输入文件名即可。
            <br/>需要更新核心？点击下方“文件工具”
          </div>
        </div>
        <div class="setting-control">
          <t-input v-model="formData.core" placeholder="例如 server.jar">
            <template #suffix>
              <t-button variant="text" theme="primary" size="small" @click="showCoreTools = !showCoreTools">
                {{ showCoreTools ? '收起工具' : '文件工具' }}
              </t-button>
            </template>
          </t-input>

          <div v-if="showCoreTools" class="core-tools-panel">
            <t-space direction="vertical" style="width: 100%">
              <t-alert theme="info" message="在此处操作会自动下载/上传文件，并填入上方的文件名。" />

              <t-row :gutter="12">
                <t-col :span="6">
                  <t-button block variant="outline" @click="showCoreSelector = true">
                    <template #icon><t-icon name="cloud-download" /></template>
                    从版本库下载
                  </t-button>
                </t-col>
                <t-col :span="6">
                  <t-button block variant="outline" :loading="isUploading" @click="triggerFileSelect">
                    <template #icon><t-icon name="upload" /></template>
                    上传本地文件
                  </t-button>
                </t-col>
              </t-row>

              <input ref="uploadInputRef" type="file" accept=".jar" hidden @change="onFileChange" />

              <div v-if="isUploading" class="upload-progress">
                <span>正在上传: {{ uploadedFileName }}</span>
                <t-progress theme="line" :percentage="uploadProgress" />
              </div>
            </t-space>
          </div>
        </div>
      </div>

      <div class="setting-group-title">资源限制</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">内存分配 (MB)</div>
          <div class="desc">设置 Java 堆内存大小 (Xms / Xmx)</div>
        </div>
        <div class="setting-control flex-row">
          <t-input-number
            v-model="formData.minM"
            :min="0"
            placeholder="最小(Xms)"
            theme="column"
            style="width: 140px"
            suffix="MB"
          />
          <span class="separator">-</span>
          <t-input-number
            v-model="formData.maxM"
            :min="0"
            placeholder="最大(Xmx)"
            theme="column"
            style="width: 140px"
            suffix="MB"
          />
        </div>
      </div>

      <div class="setting-group-title">自动备份</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">备份策略</div>
          <div class="desc">设置自动备份保留数量，以及触发备份的延迟时间</div>
        </div>
        <div class="setting-control flex-row">
          <t-input-number
            v-model="formData.backupMaxCount"
            :min="1"
            :max="100"
            placeholder="保留份数"
            theme="column"
            style="width: 140px"
            suffix="份"
          />
          <span class="separator">/</span>
          <t-input-number
            v-model="formData.backupDelay"
            :min="0"
            placeholder="延迟时间"
            theme="column"
            style="width: 140px"
            suffix="秒"
          />
        </div>
        <div class="setting-info-desc-only">
          <t-tooltip content="MSLX 向服务器发送 save-all 指令后，会等待指定的秒数，确保数据完全写入硬盘后再开始打包备份。">
                 <span style="font-size: 12px; color: var(--td-text-color-placeholder); cursor: help">
                     <t-icon name="help-circle" /> 什么是延迟时间？
                 </span>
          </t-tooltip>
        </div>
      </div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">备份存放路径</div>
          <div class="desc">选择备份文件存储的位置。推荐存储在实例文件夹外部以免误删。</div>
        </div>
        <div class="setting-control">
          <t-select v-model="backupLocationType" :options="[
            { label: '实例文件夹内 (Instance)', value: 'MSLX://Backup/Instance' },
            { label: '全局数据目录 (Data)', value: 'MSLX://Backup/Data' },
            { label: '自定义绝对路径', value: 'custom' }
          ]" />

          <div style="margin-top: 8px" v-if="backupLocationType === 'custom'">
            <t-input v-model="customBackupPath" placeholder="输入备份存放的绝对路径" />
          </div>
        </div>
      </div>

      <div class="setting-group-title">外置登录</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">Yggdrasil API</div>
          <div class="desc">
            选择认证服务器。留空则表示使用官方正版登录 (或离线模式)。
          </div>
        </div>
        <div class="setting-control">
          <t-select v-model="authSelectType" :options="authOptions" />

          <div style="margin-top: 8px" v-if="authSelectType === 'custom'">
            <t-input v-model="customAuthUrl" placeholder="输入 Authlib-Injector API 地址" />
          </div>
        </div>
      </div>

      <div class="setting-group-title">高级设置</div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">自动重启</div>
          <div class="desc">当服务器崩溃或意外停止时尝试自动重启</div>
        </div>
        <div class="setting-control">
          <t-switch v-model="formData.autoRestart" :label="['已开启', '已关闭']" />
        </div>
      </div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">随守护进程启动</div>
          <div class="desc">当物理机开机/面板启动时，自动启动此实例</div>
        </div>
        <div class="setting-control">
          <t-switch v-model="formData.runOnStartup" :label="['已开启', '已关闭']" />
        </div>
      </div>

      <div class="setting-item">
        <div class="setting-info">
          <div class="title">控制台编码</div>
          <div class="desc">设置输入输出流的字符集，乱码时请尝试切换</div>
        </div>
        <div class="setting-control flex-row">
          <t-select v-model="formData.inputEncoding" :options="encodingOptions" label="输入" style="width: 140px" />
          <t-select v-model="formData.outputEncoding" :options="encodingOptions" label="输出" style="width: 140px" />
        </div>
      </div>

      <div class="form-actions">
        <t-button theme="primary" type="submit" size="large" :loading="submitting">保存设置</t-button>
        <t-button theme="default" variant="base" size="large" @click="initData">重置更改</t-button>
      </div>

    </t-form>

    <server-core-selector v-model:visible="showCoreSelector" @confirm="onCoreSelected" />

    <t-dialog
      v-model:visible="showProgressDialog"
      header="正在应用更新"
      :footer="false"
      :close-on-overlay-click="false"
      :close-btn="false"
      width="600px"
    >
      <div class="progress-body">
        <t-progress theme="plump" :percentage="progressPercent" :label="`${progressPercent}%`" />
        <div id="update-log-box" class="log-box">
          <div v-for="(log, idx) in progressLogs" :key="idx" class="log-line">
            <span class="time">{{ log.time }}</span> {{ log.msg }}
          </div>
        </div>
        <div v-if="progressPercent === 100" class="dialog-actions">
          <t-button theme="primary" @click="showProgressDialog = false">关闭并刷新</t-button>
        </div>
      </div>
    </t-dialog>
  </div>
</template>

<style scoped lang="less">
.settings-container {
  margin: 0 auto;
}

.setting-group-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--td-text-color-primary);
  margin-top: 32px;
  margin-bottom: 16px;
  padding-bottom: 8px;
  border-bottom: 1px dashed var(--td-component-stroke);
  display: flex;
  align-items: center;

  &::before {
    content: '';
    display: inline-block;
    width: 4px;
    height: 16px;
    background-color: var(--td-brand-color);
    margin-right: 8px;
    border-radius: 2px;
  }
}

.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 0;
  flex-wrap: wrap; /* 允许在手机端换行 */

  .setting-info {
    flex: 1;
    padding-right: 32px;
    min-width: 200px; /* 防止过窄 */

    .title {
      font-size: 14px;
      color: var(--td-text-color-primary);
      font-weight: 500;
      line-height: 22px;
    }

    .desc {
      font-size: 12px;
      color: var(--td-text-color-placeholder);
      margin-top: 4px;
      line-height: 20px;
    }

    .text-warning {
      color: var(--td-warning-color);
    }
  }

  /* 专门用于描述信息的占位 */
  .setting-info-desc-only {
    width: 100%;
    margin-top: 8px;
  }

  .setting-control {
    width: 340px; /* 稍微加宽一点适配内容 */
    flex-shrink: 0;

    &.flex-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .separator {
      color: var(--td-text-color-placeholder);
    }
  }
}

.core-tools-panel {
  margin-top: 12px;
  padding: 12px;
  background-color: var(--td-bg-color-secondarycontainer);
  border-radius: var(--td-radius-medium);
  border: 1px solid var(--td-component-stroke);
}

.upload-progress {
  margin-top: 8px;
  font-size: 12px;
  color: var(--td-text-color-secondary);
}

.form-actions {
  margin-top: 40px;
  display: flex;
  gap: 16px;
  padding-top: 24px;
  border-top: 1px solid var(--td-component-stroke);
}

.log-box {
  margin-top: 16px;
  height: 200px;
  background-color: #181818;
  border-radius: var(--td-radius-medium);
  padding: 12px;
  overflow-y: auto;
  font-family: 'Consolas', monospace;
  font-size: 12px;

  .log-line {
    margin-bottom: 4px;
    color: #e0e0e0;
    .time {
      color: #666;
      margin-right: 8px;
      user-select: none;
    }
  }
}

.dialog-actions {
  margin-top: 16px;
  text-align: right;
}

@media (max-width: 768px) {
  .setting-item {
    flex-direction: column;

    .setting-info {
      padding-right: 0;
      margin-bottom: 12px;
    }

    .setting-control {
      width: 100%;
    }
  }
}
</style>
