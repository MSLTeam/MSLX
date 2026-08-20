<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin, Loading as TLoading } from 'tdesign-vue-next';
import { Chat as TChat, ChatItem as TChatItem, ChatSender as TChatSender } from '@tdesign-vue-next/chat';
import '@tdesign-vue-next/chat/es/style/index.css';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from '@vueuse/core';
import { useUserStore } from '@/store';
import { sendAiChatStream, abortAiChatStream, ChatMessage, confirmAiToolAction } from '@/api/aiApi';

const props = defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: 'update:visible', val: boolean): void;
}>();

const route = useRoute();
const userStore = useUserStore();
const isDark = useDark();
const mdTheme = ref<Themes>(isDark.value ? 'dark' : 'light');
watch(isDark, () => {
  mdTheme.value = isDark.value ? 'dark' : 'light';
});

interface DisplayMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  time?: string;
  toolData?: { tool: string; data: any };
  toolExpanded?: string[];
}

const AI_CHAT_HISTORY_KEY = 'mslx_ai_chat_history';

const displayMode = ref<'drawer' | 'floating'>(
  (localStorage.getItem('mslx_ai_display_mode') as 'drawer' | 'floating') || 'drawer'
);

const floatPos = ref<{ x: number; y: number }>({
  x: Math.max(20, window.innerWidth - 580),
  y: 80,
});

let isDragging = false;
let dragOffset = { x: 0, y: 0 };

const startDrag = (e: MouseEvent) => {
  const target = e.target as HTMLElement;
  if (target.closest('.header-actions') || target.closest('.clear-btn') || target.closest('.t-button')) return;
  isDragging = true;
  dragOffset.x = e.clientX - floatPos.value.x;
  dragOffset.y = e.clientY - floatPos.value.y;
  window.addEventListener('mousemove', onDrag);
  window.addEventListener('mouseup', stopDrag);
};

const onDrag = (e: MouseEvent) => {
  if (!isDragging) return;
  const newX = Math.max(0, Math.min(window.innerWidth - 300, e.clientX - dragOffset.x));
  const newY = Math.max(0, Math.min(window.innerHeight - 150, e.clientY - dragOffset.y));
  floatPos.value = { x: newX, y: newY };
};

const stopDrag = () => {
  isDragging = false;
  window.removeEventListener('mousemove', onDrag);
  window.removeEventListener('mouseup', stopDrag);
};

const toggleDisplayMode = () => {
  displayMode.value = displayMode.value === 'drawer' ? 'floating' : 'drawer';
  localStorage.setItem('mslx_ai_display_mode', displayMode.value);
};

const SUGGESTIONS_MARKER_REGEX = /<<<SUGGESTIONS:\[.*?\]>>>/g;
const DSML_TOOL_CALLS_REGEX = /<｜DSML｜tool_calls[\s\S]*?<\/｜DSML｜tool_calls>/g;
const DSML_INVOKE_REGEX = /<｜DSML｜invoke[\s\S]*?<\/｜DSML｜invoke>/g;

const stripStreamMarkers = (raw: string): string =>
  raw.replace(SUGGESTIONS_MARKER_REGEX, '').replace(DSML_TOOL_CALLS_REGEX, '').replace(DSML_INVOKE_REGEX, '');

const cleanStreamedContent = (raw: string): string => stripStreamMarkers(raw).trim();

const formatTime = (d: Date = new Date()): string => {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

const defaultWelcomeMessage: DisplayMessage = {
  id: 'welcome',
  role: 'assistant',
  content: '你好！我是 MSLX AI 智能运维助手。你可以随时与我交流，例如切换 Java 版本、修改参数、重启服务器或创建新的 Minecraft 服务端。',
};

const getCurrentServerId = (): number | null => {
  try {
    if (route && route.params && route.params.id) {
      const idNum = parseInt(String(route.params.id), 10);
      if (!isNaN(idNum) && idNum > 0) {
        return idNum;
      }
    }
    const match = route.path?.match(/\/instance\/[^\/]+\/(\d+)/);
    if (match) {
      return parseInt(match[1], 10);
    }
  } catch (e) {
    console.warn('获取当前页面 Server ID 失败:', e);
  }
  return null;
};

const loadHistory = (): DisplayMessage[] => {
  try {
    const saved = localStorage.getItem(AI_CHAT_HISTORY_KEY);
    if (saved) {
      const parsed = JSON.parse(saved);
      if (Array.isArray(parsed) && parsed.length > 0) {
        const cleaned = parsed
          .filter((m) => !(m.role === 'assistant' && m.content === '正在思考与处理...' && !m.toolData))
          .map((m) => {
            if (m.role === 'assistant' && m.content === '正在思考与处理...') {
              return { ...m, content: '⚠️ 上次对话因页面刷新而中断。' };
            }
            if (m.role === 'assistant') {
              return { ...m, content: cleanStreamedContent(m.content) };
            }
            return m;
          });
        if (cleaned.length > 0) return cleaned;
      }
    }
  } catch (e) {
    console.error('加载 AI 聊天历史记录异常:', e);
  }
  return [defaultWelcomeMessage];
};

const chatList = ref<DisplayMessage[]>(loadHistory());
const inputText = ref('');
const loading = ref(false);
const suggestedReplies = ref<string[]>([]);
const confirmingToolId = ref<string | null>(null);
const activeStreamingId = ref<string | null>(null);

watch(
  chatList,
  (newVal) => {
    try {
      const toSave = newVal.filter(
        (m) => !(m.role === 'assistant' && m.content === '正在思考与处理...' && !m.toolData)
      );
      localStorage.setItem(AI_CHAT_HISTORY_KEY, JSON.stringify(toSave));
    } catch (e) {
      console.error('保存 AI 聊天历史失败:', e);
    }
  },
  { deep: true }
);

const handleClose = () => {
  emit('update:visible', false);
};

const handleClearHistory = () => {
  if (loading.value) {
    handleAbort();
  }
  chatList.value = [{ ...defaultWelcomeMessage, id: `welcome_${Date.now()}` }];
  suggestedReplies.value = [];
  localStorage.removeItem(AI_CHAT_HISTORY_KEY);
  MessagePlugin.success('AI 对话历史已成功清空');
};

const handleAbort = () => {
  if (!loading.value) return;
  abortAiChatStream();
  loading.value = false;

  const lastMsg = chatList.value[chatList.value.length - 1];
  if (lastMsg && lastMsg.role === 'assistant') {
    if (lastMsg.content === '正在思考与处理...') {
      lastMsg.content = '🛑 对话已被用户主动中断。';
    } else {
      lastMsg.content += '\n\n🛑 对话已被用户主动中断。';
    }
  }
  MessagePlugin.warning('已手动中断 AI 对话响应');
};

const cleanToolResultText = (text: string): string =>
  text.replace(/^\[(?:SUCCESS|ERROR|INFO|CANCELLED|PENDING_CONFIRMATION|REQUIRES_CONFIRMATION)\]\s*/, '');

const getToolFriendlyName = (toolName?: string): string => {
  if (!toolName) return '思考与处理';
  const map: Record<string, string> = {
    create_server: '下载与部署服务端核心',
    create_mc_server: '下载与部署服务端核心',
    query_available_server_cores: '查询可用服务端核心',
    query_java_environments: '扫描 Java 运行环境',
    query_creation_status: '查询开服进度',
    update_instance_settings: '修改服务器配置',
    update_server_config: '更新服务端配置文件',
    list_server_files: '读取服务器文件列表',
    read_server_file: '读取文件内容',
    read_server_log: '读取崩溃/运行日志',
    copy_server_file: '复制文件与目录',
    move_server_file: '移动/重命名文件',
    query_system_metrics: '监测系统与实例性能',
    list_server_mods_plugins: '读取 Mod/插件列表',
    write_server_file: '写入/修改文件内容',
    delete_server_file: '删除服务器文件',
    control_server: '控制服务器状态(启动/停止/重启)',
    query_server_status: '查询服务器在线状态',
  };
  return map[toolName] || toolName;
};

const appendToolResult = (msg: DisplayMessage, resultText: string) => {
  const text = cleanToolResultText(resultText) || '操作已处理。';
  if (!msg.content || msg.content === '正在思考与处理...') {
    msg.content = text;
  } else {
    msg.content = `${msg.content}\n\n${text}`;
  }
};

const handleConfirmToolAction = async (msg: DisplayMessage) => {
  if (!msg.toolData?.data?.confirmationId || confirmingToolId.value) return;
  confirmingToolId.value = msg.id;
  try {
    const result = await confirmAiToolAction(msg.toolData.data.confirmationId, true);
    const resultData = (result.data || {}) as Record<string, any>;
    msg.toolData.data = { ...msg.toolData.data, ...resultData, confirmed: true };
    appendToolResult(msg, result.message);
    if (result.message && result.message.includes('[ERROR]')) {
      MessagePlugin.error(cleanToolResultText(result.message));
    } else {
      MessagePlugin.success('已确认授权，操作已执行');
    }
  } catch (e: any) {
    MessagePlugin.error(`确认失败: ${e.message || e}`);
  } finally {
    confirmingToolId.value = null;
  }
};

const handleRejectToolAction = async (msg: DisplayMessage) => {
  if (!msg.toolData?.data?.confirmationId || confirmingToolId.value) return;
  confirmingToolId.value = msg.id;
  try {
    const result = await confirmAiToolAction(msg.toolData.data.confirmationId, false);
    msg.toolData.data = { ...msg.toolData.data, rejected: true, confirmed: false };
    appendToolResult(msg, result.message || '已拒绝该敏感操作。');
    MessagePlugin.info('已拒绝该敏感文件操作');
  } catch (e: any) {
    MessagePlugin.error(`操作失败: ${e.message || e}`);
  } finally {
    confirmingToolId.value = null;
  }
};

const handleQuickSend = (text: string) => {
  inputText.value = text;
  handleSend();
};

const handleCopyMessage = async (msg: DisplayMessage) => {
  try {
    await navigator.clipboard.writeText(msg.content);
    MessagePlugin.success('已复制回复内容');
  } catch {
    MessagePlugin.error('复制失败');
  }
};

const handleReplayMessage = (msg: DisplayMessage) => {
  const idx = chatList.value.findIndex((m) => m.id === msg.id);
  if (idx <= 0) {
    MessagePlugin.warning('没有可重发的上一条用户消息');
    return;
  }
  const prevUser = chatList.value[idx - 1];
  if (!prevUser || prevUser.role !== 'user') {
    MessagePlugin.warning('没有可重发的上一条用户消息');
    return;
  }
  chatList.value = chatList.value.slice(0, idx);
  inputText.value = prevUser.content;
  handleSend();
};

const handleSend = async (value?: string) => {
  const query = (value ?? inputText.value).trim();
  if (!query || loading.value) return;

  const userMsgId = `user_${Date.now()}`;
  chatList.value.push({
    id: userMsgId,
    role: 'user',
    content: query,
    time: formatTime(),
  });

  inputText.value = '';
  loading.value = true;

  const botMsgId = `bot_${Date.now()}`;
  chatList.value.push({
    id: botMsgId,
    role: 'assistant',
    content: '正在思考与处理...',
    time: formatTime(),
    toolExpanded: [],
  });
  activeStreamingId.value = botMsgId;

  const activeServerId = getCurrentServerId();

  const apiMessages: ChatMessage[] = [
    ...(activeServerId
      ? [
          {
            role: 'system' as const,
            content: `【当前用户界面上下文】：用户目前正处于服务器 (ID: ${activeServerId}) 的控制台或设置页面。如果用户提出的操作指令未显式指明服务器 ID（如说“把 Java 切换成 25”、“重启服务器”、“修改端口”），请直接默认作用于该服务器 (ID: ${activeServerId})！`,
          },
        ]
      : []),
    ...chatList.value
      .filter((m) => m.id !== botMsgId)
      .map((m) => ({
        role: m.role,
        content: m.content,
      })),
  ];

  let isFirstChunk = true;

  await sendAiChatStream(
    apiMessages,
    (chunk) => {
      const target = chatList.value.find((m) => m.id === botMsgId);
      if (target) {
        if (isFirstChunk) {
          target.content = stripStreamMarkers(chunk);
          isFirstChunk = false;
        } else {
          target.content = stripStreamMarkers(target.content + chunk);
        }
      }
    },
    (toolName, toolData) => {
      if (toolName === 'suggested_replies' && Array.isArray(toolData)) {
        suggestedReplies.value = toolData;
        return;
      }
      const target = chatList.value.find((m) => m.id === botMsgId);
      if (target) {
        target.toolData = { tool: toolName, data: toolData };
        target.toolExpanded = [];
      }
    },
    (err) => {
      const target = chatList.value.find((m) => m.id === botMsgId);
      activeStreamingId.value = null;
      if (target) {
        target.content = `❌ ${err}`;
      } else {
        MessagePlugin.error(err);
      }
    },
    () => {
      const target = chatList.value.find((m) => m.id === botMsgId);
      activeStreamingId.value = null;
      if (target) {
        target.content = cleanStreamedContent(target.content);
        if (target.content === '正在思考与处理...') {
          target.content = '';
        }
        target.time = formatTime();
      }
      loading.value = false;
    }
  );
};
</script>

<template>
  <div>
    <!-- 模式 1：右侧抽屉模式 -->
    <t-drawer
      v-if="displayMode === 'drawer'"
      attach="body"
      :visible="visible"
      size="560px"
      :footer="false"
      :body-style="{ background: 'var(--td-bg-color-container)', height: 'calc(100% - 48px)', padding: '16px' }"
      @close="handleClose"
    >
      <template #header>
        <div class="drawer-header-row">
          <div class="drawer-title">
            <span>MSLX AI 智能运维助手</span>
            <t-tag v-if="getCurrentServerId()" theme="primary" variant="outline" size="small" style="margin-left: 8px">
              当前选中服: #{{ getCurrentServerId() }}
            </t-tag>
          </div>
          <div class="header-actions">
            <t-tooltip content="切换至自由悬浮小窗">
              <t-button variant="text" shape="square" size="small" class="mode-btn" @click="toggleDisplayMode">
                <template #icon><t-icon name="desktop" class="mode-icon" /></template>
              </t-button>
            </t-tooltip>
            <t-button variant="text" theme="danger" size="small" class="clear-btn" @click="handleClearHistory">
              <template #icon><t-icon name="delete" /></template>
              清空历史
            </t-button>
          </div>
        </div>
      </template>

      <!-- 统一的 AI 对话核心界面组件 -->
      <div class="ai-chat-container">
        <div class="chat-list-wrapper">
          <t-chat
            class="mslx-chat-component"
            :auto-scroll="true"
            :show-scroll-button="true"
            :default-scroll-to="'bottom'"
          >
            <t-chat-item
              v-for="msg in chatList"
              :key="msg.id"
              :role="msg.role"
              :name="msg.role === 'assistant' ? 'MSLX AI' : (userStore.userInfo.name || userStore.userInfo.username || '用户')"
              :avatar="msg.role === 'assistant' ? 'https://www.mslmc.cn/logo.png' : (userStore.userInfo.avatar || 'https://tdesign.gtimg.com/site/avatar.jpg')"
              :datetime="msg.time"
              :animation="'gradient'"
            >
              <template #content>
                <!-- 用户消息文本 -->
                <div v-if="msg.role === 'user'" class="chat-bubble-text">
                  {{ msg.content }}
                </div>

                <!-- AI 回复 Markdown -->
                <template v-else>
                  <div v-if="msg.content && msg.content !== '正在思考与处理...'" class="md-preview-wrapper">
                    <md-preview
                      :editor-id="`ai-msg-${msg.id}`"
                      :model-value="msg.content"
                      :theme="mdTheme"
                      style="background: transparent; padding: 0"
                    />
                  </div>

                  <!-- 只要请求未结束(loading 为 true)，且正在对当前消息流式响应或后台调取工具，常驻展示动态思考/执行动效胶囊 -->
                  <div v-if="loading && msg.id === activeStreamingId" class="thinking-state active-thinking">
                    <t-loading
                      size="small"
                      :text="msg.toolData?.tool ? `正在后台【${getToolFriendlyName(msg.toolData.tool)}】...` : 'AI 正在思考并处理中...'"
                    />
                  </div>
                </template>

                <div
                  v-if="msg.toolData?.data?.requiresConfirmation && !msg.toolData?.data?.confirmed && !msg.toolData?.data?.rejected"
                  class="action-confirm-card danger-theme"
                >
                  <div class="card-header">
                    <div class="header-left">
                      <div class="pulse-icon danger">
                        <t-icon name="warning-circle-filled" />
                      </div>
                      <span class="card-title">需要您的授权确认</span>
                    </div>
                    <t-tag size="small" theme="danger" variant="light-outline">高风险操作</t-tag>
                  </div>
                  <div class="card-body">
                    AI 申请对文件/目录 <code class="file-code">{{ msg.toolData.data.filePath }}</code> 执行
                    <strong class="action-highlight red">
                      {{ msg.toolData.data.action === 'delete' ? '【彻底删除】' : '【覆盖修改】' }}
                    </strong> 操作。此操作不可逆！
                  </div>
                  <div class="card-footer">
                    <t-button
                      theme="danger"
                      size="small"
                      class="action-btn"
                      :loading="confirmingToolId === msg.id"
                      :disabled="!!confirmingToolId"
                      @click="handleConfirmToolAction(msg)"
                    >
                      <template #icon><t-icon name="check-circle" /></template>
                      确认授权{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}
                    </t-button>
                    <t-button
                      theme="default"
                      variant="outline"
                      size="small"
                      class="action-btn"
                      :disabled="!!confirmingToolId"
                      @click="handleRejectToolAction(msg)"
                    >
                      <template #icon><t-icon name="close-circle" /></template>
                      拒绝操作
                    </t-button>
                  </div>
                </div>

                <div v-else-if="msg.toolData?.data?.confirmed || msg.toolData?.data?.rejected" class="action-status-bar">
                  <t-tag v-if="msg.toolData?.data?.confirmed" theme="success" variant="light" size="small">
                    <template #icon><t-icon name="check-circle-filled" /></template>
                    已授权{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}: {{ msg.toolData.data.filePath }}
                  </t-tag>
                  <t-tag v-else theme="danger" variant="light" size="small">
                    <template #icon><t-icon name="close-circle-filled" /></template>
                    已拒绝{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}操作
                  </t-tag>
                </div>

                <div v-if="msg.toolData" class="tool-debug-collapse">
                  <t-collapse v-model="msg.toolExpanded" borderless size="small">
                    <t-collapse-panel value="tool_detail">
                      <template #header>
                        <div class="tool-debug-title">
                          <t-icon name="code" />
                          <span>调取工具: {{ msg.toolData.tool }}</span>
                        </div>
                      </template>
                      <pre class="tool-json-code">{{ JSON.stringify(msg.toolData.data, null, 2) }}</pre>
                    </t-collapse-panel>
                  </t-collapse>
                </div>
              </template>
              <template #actions>
                <div
                  v-if="msg.role === 'assistant' && msg.content && msg.content !== '正在思考与处理...'"
                  class="msg-actions"
                >
                  <t-tooltip content="复制">
                    <t-button
                      variant="text"
                      shape="square"
                      size="small"
                      class="msg-action-btn"
                      @click="handleCopyMessage(msg)"
                    >
                      <template #icon><t-icon name="copy" /></template>
                    </t-button>
                  </t-tooltip>
                  <t-tooltip content="重新生成">
                    <t-button
                      variant="text"
                      shape="square"
                      size="small"
                      class="msg-action-btn"
                      @click="handleReplayMessage(msg)"
                    >
                      <template #icon><t-icon name="refresh" /></template>
                    </t-button>
                  </t-tooltip>
                </div>
              </template>
            </t-chat-item>
          </t-chat>
        </div>

        <div v-if="suggestedReplies.length > 0 && !loading" class="suggested-replies-bar">
          <span class="sug-hint"><t-icon name="lightbulb" /> 快捷回复：</span>
          <t-tag
            v-for="(sug, index) in suggestedReplies"
            :key="index"
            theme="primary"
            variant="light"
            class="sug-chip"
            @click="handleQuickSend(sug)"
          >
            {{ sug }}
          </t-tag>
        </div>

        <div class="chat-input-area">
          <t-chat-sender
            v-model="inputText"
            :loading="loading"
            placeholder="请输入指令，如“把 Java 切换成 25”、“重启服务器”..."
            @send="handleSend"
            @stop="handleAbort"
          />
        </div>
      </div>
    </t-drawer>

    <!-- 模式 2：自由悬浮拖拽小窗模式 (使用 Teleport 直接挂载至 Document Body) -->
    <teleport to="body">
      <div
        v-if="visible && displayMode === 'floating'"
        class="floating-ai-card"
        :style="{ left: `${floatPos.x}px`, top: `${floatPos.y}px` }"
      >
        <div class="floating-header" @mousedown="startDrag">
          <div class="drawer-title draggable-title">
            <t-icon name="move" class="drag-icon" />
            <span>MSLX AI 智能运维助手</span>
            <t-tag v-if="getCurrentServerId()" theme="primary" variant="outline" size="small" style="margin-left: 8px">
              当前选中服: #{{ getCurrentServerId() }}
            </t-tag>
          </div>
          <div class="header-actions">
            <t-tooltip content="切换至侧边抽屉">
              <t-button variant="text" shape="square" size="small" class="mode-btn" @click="toggleDisplayMode">
                <template #icon><t-icon name="view-list" class="mode-icon" /></template>
              </t-button>
            </t-tooltip>
            <t-button variant="text" theme="danger" size="small" class="clear-btn" @click="handleClearHistory">
              <template #icon><t-icon name="delete" /></template>
              清空历史
            </t-button>
            <t-button variant="text" shape="square" size="small" class="close-btn" @click="handleClose">
              <template #icon><t-icon name="close" /></template>
            </t-button>
          </div>
        </div>

        <div class="floating-body">
          <div class="ai-chat-container">
            <div class="chat-list-wrapper">
              <t-chat
                class="mslx-chat-component"
                :auto-scroll="true"
                :show-scroll-button="true"
                :default-scroll-to="'bottom'"
              >
                <t-chat-item
                  v-for="msg in chatList"
                  :key="msg.id"
                  :role="msg.role"
                  :name="msg.role === 'assistant' ? 'MSLX AI' : (userStore.userInfo.name || userStore.userInfo.username || '用户')"
                  :avatar="msg.role === 'assistant' ? 'https://www.mslmc.cn/logo.png' : (userStore.userInfo.avatar || 'https://tdesign.gtimg.com/site/avatar.jpg')"
                  :datetime="msg.time"
                  :animation="'gradient'"
                >
                  <template #content>
                    <!-- 用户消息文本 -->
                    <div v-if="msg.role === 'user'" class="chat-bubble-text">
                      {{ msg.content }}
                    </div>

                    <!-- AI 回复 Markdown -->
                    <template v-else>
                      <div v-if="msg.content && msg.content !== '正在思考与处理...'" class="md-preview-wrapper">
                        <md-preview
                          :editor-id="`ai-msg-float-${msg.id}`"
                          :model-value="msg.content"
                          :theme="mdTheme"
                          style="background: transparent; padding: 0"
                        />
                      </div>

                      <!-- 只要请求未结束(loading 为 true)，且正在对当前消息流式响应或后台调取工具，常驻展示动态思考/执行动效胶囊 -->
                      <div v-if="loading && msg.id === activeStreamingId" class="thinking-state active-thinking">
                        <t-loading
                          size="small"
                          :text="msg.toolData?.tool ? `正在后台【${getToolFriendlyName(msg.toolData.tool)}】...` : 'AI 正在思考并处理中...'"
                        />
                      </div>
                    </template>

                    <div
                      v-if="msg.toolData?.data?.requiresConfirmation && !msg.toolData?.data?.confirmed && !msg.toolData?.data?.rejected"
                      class="action-confirm-card danger-theme"
                    >
                      <div class="card-header">
                        <div class="header-left">
                          <div class="pulse-icon danger">
                            <t-icon name="warning-circle-filled" />
                          </div>
                          <span class="card-title">需要您的授权确认</span>
                        </div>
                        <t-tag size="small" theme="danger" variant="light-outline">高风险操作</t-tag>
                      </div>
                      <div class="card-body">
                        AI 申请对文件/目录 <code class="file-code">{{ msg.toolData.data.filePath }}</code> 执行
                        <strong class="action-highlight red">
                          {{ msg.toolData.data.action === 'delete' ? '【彻底删除】' : '【覆盖修改】' }}
                        </strong> 操作。此操作不可逆！
                      </div>
                      <div class="card-footer">
                        <t-button
                          theme="danger"
                          size="small"
                          class="action-btn"
                          :loading="confirmingToolId === msg.id"
                          :disabled="!!confirmingToolId"
                          @click="handleConfirmToolAction(msg)"
                        >
                          <template #icon><t-icon name="check-circle" /></template>
                          确认授权{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}
                        </t-button>
                        <t-button
                          theme="default"
                          variant="outline"
                          size="small"
                          class="action-btn"
                          :disabled="!!confirmingToolId"
                          @click="handleRejectToolAction(msg)"
                        >
                          <template #icon><t-icon name="close-circle" /></template>
                          拒绝操作
                        </t-button>
                      </div>
                    </div>

                    <div v-else-if="msg.toolData?.data?.confirmed || msg.toolData?.data?.rejected" class="action-status-bar">
                      <t-tag v-if="msg.toolData?.data?.confirmed" theme="success" variant="light" size="small">
                        <template #icon><t-icon name="check-circle-filled" /></template>
                        已授权{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}: {{ msg.toolData.data.filePath }}
                      </t-tag>
                      <t-tag v-else theme="danger" variant="light" size="small">
                        <template #icon><t-icon name="close-circle-filled" /></template>
                        已拒绝{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}操作
                      </t-tag>
                    </div>

                    <div v-if="msg.toolData" class="tool-debug-collapse">
                      <t-collapse v-model="msg.toolExpanded" borderless size="small">
                        <t-collapse-panel value="tool_detail">
                          <template #header>
                            <div class="tool-debug-title">
                              <t-icon name="code" />
                              <span>调取工具: {{ msg.toolData.tool }}</span>
                            </div>
                          </template>
                          <pre class="tool-json-code">{{ JSON.stringify(msg.toolData.data, null, 2) }}</pre>
                        </t-collapse-panel>
                      </t-collapse>
                    </div>
                  </template>
                  <template #actions>
                    <div
                      v-if="msg.role === 'assistant' && msg.content && msg.content !== '正在思考与处理...'"
                      class="msg-actions"
                    >
                      <t-tooltip content="复制">
                        <t-button
                          variant="text"
                          shape="square"
                          size="small"
                          class="msg-action-btn"
                          @click="handleCopyMessage(msg)"
                        >
                          <template #icon><t-icon name="copy" /></template>
                        </t-button>
                      </t-tooltip>
                      <t-tooltip content="重新生成">
                        <t-button
                          variant="text"
                          shape="square"
                          size="small"
                          class="msg-action-btn"
                          @click="handleReplayMessage(msg)"
                        >
                          <template #icon><t-icon name="refresh" /></template>
                        </t-button>
                      </t-tooltip>
                    </div>
                  </template>
                </t-chat-item>
              </t-chat>
            </div>

            <div v-if="suggestedReplies.length > 0 && !loading" class="suggested-replies-bar">
              <span class="sug-hint"><t-icon name="lightbulb" /> 快捷回复：</span>
              <t-tag
                v-for="(sug, index) in suggestedReplies"
                :key="index"
                theme="primary"
                variant="light"
                class="sug-chip"
                @click="handleQuickSend(sug)"
              >
                {{ sug }}
              </t-tag>
            </div>

            <div class="chat-input-area">
              <t-chat-sender
                v-model="inputText"
                :loading="loading"
                placeholder="请输入指令，如“把 Java 切换成 25”、“重启服务器”..."
                @send="handleSend"
                @stop="handleAbort"
              />
            </div>
          </div>
        </div>
      </div>
    </teleport>
  </div>
</template>

<style scoped>
.drawer-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding-right: 12px;
}

.drawer-title {
  display: flex;
  align-items: center;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.mode-btn {
  color: var(--td-text-color-primary) !important;
}

.mode-icon {
  font-size: 16px !important;
  color: var(--td-text-color-primary) !important;
}

/* 自由悬浮小窗样式：纯实心高不透明背景，消除背景内容透穿与字迹模糊 */
.floating-ai-card {
  position: fixed;
  z-index: 2000;
  width: 540px;
  height: 660px;
  background-color: var(--td-bg-color-container) !important;
  border: 1px solid var(--td-component-border);
  border-radius: 16px;
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.28);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  opacity: 1 !important;
}

.floating-header {
  padding: 12px 16px;
  background-color: var(--td-bg-color-secondarycontainer) !important;
  border-bottom: 1px solid var(--td-component-border);
  cursor: move;
  user-select: none;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.floating-body {
  flex: 1;
  min-height: 0;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background-color: var(--td-bg-color-container) !important;
}

.draggable-title {
  display: flex;
  align-items: center;
  cursor: move;
}

.drag-icon {
  margin-right: 6px;
  color: var(--td-brand-color);
}

.ai-chat-container {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.chat-list-wrapper {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 12px 0;
  position: relative;
}

.mslx-chat-component {
  width: 100%;
  height: 100%;
}

/* 优雅重置与定位【划到最下面】(t-chat__to-bottom) 悬浮按钮，消除 left:50% 死板居中与双重圆角嵌套 */
.mslx-chat-component :deep(.t-chat__to-bottom) {
  position: absolute !important;
  left: auto !important;
  right: 24px !important;
  margin-left: 0 !important;
  bottom: 16px !important;
  z-index: 50 !important;
  width: 40px !important;
  height: 40px !important;
  border-radius: 50% !important;
  padding: 0 !important;
  border: none !important;
  background: transparent !important;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15) !important;
  overflow: hidden !important;
  transition: all 0.2s cubic-bezier(0.38, 0, 0.24, 1) !important;
}

.mslx-chat-component :deep(.t-chat__to-bottom-inner) {
  width: 40px !important;
  height: 40px !important;
  border-radius: 50% !important;
  border: 1px solid var(--td-component-border) !important;
  background: var(--td-bg-color-container) !important;
  box-sizing: border-box !important;
}

.mslx-chat-component :deep(.t-chat__to-bottom:hover) {
  transform: translateY(-2px) !important;
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.25) !important;
}

.chat-bubble-text {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
}

.md-preview-wrapper {
  font-size: 14px;
}

/* 精致思考/加载动画胶囊 */
.thinking-state {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border-radius: 10px;
  background: var(--td-bg-color-component);
  border: 1px solid var(--td-component-border);
  margin-top: 4px;
}

.active-thinking {
  margin-top: 8px;
  background: var(--td-brand-color-light);
  border-color: var(--td-brand-color-light-hover);
  color: var(--td-brand-color);
}

/* 消息操作栏（复制 / 重新生成） */
.msg-actions {
  display: flex;
  gap: 2px;
  margin-top: 4px;
  opacity: 0.7;
  transition: opacity 0.2s ease;
}

.msg-actions:hover {
  opacity: 1;
}

.msg-action-btn {
  color: var(--td-text-color-placeholder);
}

/* 现代化高风险操作卡片样式 */
.action-confirm-card {
  margin-top: 12px;
  padding: 14px 16px;
  border-radius: 12px;
  background: var(--td-bg-color-container);
  border: 1px solid var(--td-error-color-3);
  box-shadow: 0 4px 16px rgba(239, 68, 68, 0.08);
  transition: all 0.25s ease;
}

.action-confirm-card.danger-theme {
  border-left: 4px solid var(--td-error-color);
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.pulse-icon.danger {
  font-size: 18px;
  color: var(--td-error-color);
  display: flex;
  align-items: center;
}

.card-title {
  font-weight: 600;
  font-size: 13px;
  color: var(--td-text-color-primary);
}

.card-body {
  font-size: 13px;
  line-height: 1.6;
  color: var(--td-text-color-secondary);
  margin-bottom: 12px;
}

.file-code {
  background: var(--td-bg-color-component);
  color: var(--td-text-color-primary);
  padding: 2px 6px;
  border-radius: 6px;
  font-family: monospace;
  font-weight: 600;
  border: 1px solid var(--td-component-border);
}

.action-highlight.red {
  color: var(--td-error-color);
}

.card-footer {
  display: flex;
  gap: 10px;
}

.action-btn {
  border-radius: 8px;
}

.action-status-bar {
  margin-top: 8px;
}

/* 工具调试日志（极简且默认收起） */
.tool-debug-collapse {
  margin-top: 8px;
  opacity: 0.7;
}

.tool-debug-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  color: var(--td-text-color-placeholder);
}

.tool-json-code {
  font-family: monospace;
  font-size: 11px;
  margin: 0;
  padding: 8px;
  background-color: var(--td-bg-color-page);
  border-radius: 6px;
  color: var(--td-text-color-secondary);
  white-space: pre-wrap;
  word-break: break-all;
}

.suggested-replies-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 8px 0;
  margin-top: 4px;
  margin-bottom: 4px;
}

.sug-hint {
  font-size: 12px;
  color: var(--td-brand-color);
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 4px;
}

.sug-chip {
  cursor: pointer;
  user-select: none;
  transition: all 0.2s ease;
}

.sug-chip:hover {
  transform: translateY(-1px);
  opacity: 0.9;
}

/* 彻底消除双层嵌套边框，将 .t-chat-sender 打造为唯一单层卡片 */
.chat-input-area {
  padding-top: 12px;
  border-top: 1px solid var(--td-component-border);
}

.chat-input-area :deep(.t-chat-sender) {
  position: relative !important;
  border: 1px solid var(--td-component-border) !important;
  border-radius: 16px !important;
  background-color: var(--td-bg-color-container) !important;
  padding: 12px 14px 8px 14px !important;
  box-sizing: border-box !important;
  transition: border-color 0.2s ease, box-shadow 0.2s ease !important;
}

.chat-input-area :deep(.t-chat-sender:focus-within) {
  border-color: var(--td-brand-color) !important;
  box-shadow: 0 0 0 2px var(--td-brand-color-focus) !important;
}

/* 彻底剥离所有内部元素的边框、阴影、背景与聚焦边框 */
.chat-input-area :deep(.t-chat-sender__textarea),
.chat-input-area :deep(.t-textarea),
.chat-input-area :deep(.t-textarea__inner),
.chat-input-area :deep(textarea) {
  border: none !important;
  outline: none !important;
  box-shadow: none !important;
  background: transparent !important;
  border-radius: 0 !important;
  padding: 0 !important;
  margin: 0 !important;
}

.chat-input-area :deep(.t-chat-sender__textarea--focus),
.chat-input-area :deep(.t-textarea__inner.t-is-focused) {
  border: none !important;
  box-shadow: none !important;
}

.chat-input-area :deep(textarea) {
  font-size: 14px !important;
  line-height: 1.6 !important;
  color: var(--td-text-color-primary) !important;
  min-height: 48px !important;
}

.chat-input-area :deep(.t-chat-sender__footer) {
  display: flex !important;
  align-items: center !important;
  justify-content: flex-end !important;
  margin-top: 4px !important;
  padding-top: 0 !important;
  border: none !important;
  background: transparent !important;
}

.chat-input-area :deep(.t-chat-sender__upload) {
  display: none !important;
}

/* 解决发送按钮内部图标被组件库原生 display:none 隐藏的问题 */
.chat-input-area :deep(.t-chat-sender__button__default > div) {
  display: flex !important;
  align-items: center !important;
  justify-content: center !important;
  width: 100% !important;
  height: 100% !important;
}

.chat-input-area :deep(.t-chat-sender__button__default) {
  background-color: var(--td-brand-color) !important;
  color: #ffffff !important;
}

.chat-input-area :deep(.t-chat-sender__button__default .t-icon),
.chat-input-area :deep(.t-chat-sender__button__default svg) {
  color: #ffffff !important;
  fill: currentColor !important;
  display: inline-block !important;
  opacity: 1 !important;
}

/* 彻底隐藏 TChat 组件内置在对话列表底部自带的“清空历史”分割线与按钮 */
.mslx-chat-component :deep(.t-chat__list .clear-btn),
.mslx-chat-component :deep(.clear-btn-text),
.mslx-chat-component :deep(.t-chat__list .t-divider) {
  display: none !important;
}
</style>
