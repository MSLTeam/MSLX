<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { MessagePlugin, Loading as TLoading } from 'tdesign-vue-next';
import { Chat as TChat, ChatItem as TChatItem } from '@tdesign-vue-next/chat';
import '@tdesign-vue-next/chat/es/style/index.css';
import { MdPreview, type Themes } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';
import { useDark } from '@vueuse/core';
import { useUserStore } from '@/store';
import { sendAiChatStream, abortAiChatStream, ChatMessage } from '@/api/aiApi';

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
  toolData?: { tool: string; data: any };
  toolExpanded?: string[];
}

const AI_CHAT_HISTORY_KEY = 'mslx_ai_chat_history';

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

const handleConfirmToolAction = (msg: DisplayMessage) => {
  if (!msg.toolData || !msg.toolData.data) return;
  msg.toolData.data.confirmed = true;
  const data = msg.toolData.data;

  inputText.value = `【用户已确认】我已授权执行此敏感操作：对服务器 (ID: ${data.serverId}) 的文件/目录 ${data.filePath} 执行 ${data.action === 'delete' ? '删除' : '写入/覆盖'}。确认参数 confirmed: true。请立即完成操作！`;
  handleSend();
};

const handleRejectToolAction = (msg: DisplayMessage) => {
  if (!msg.toolData || !msg.toolData.data) return;
  msg.toolData.data.rejected = true;
  MessagePlugin.info('已拒绝该敏感文件操作');

  inputText.value = `【用户已拒绝】我拒绝了关于 ${msg.toolData.data.filePath} 的 ${msg.toolData.data.action === 'delete' ? '删除' : '覆盖编辑'} 请求。请取消该操作，不要修改文件。`;
  handleSend();
};

const handleQuickSend = (text: string) => {
  inputText.value = text;
  handleSend();
};

const handleSend = async () => {
  const query = inputText.value.trim();
  if (!query || loading.value) return;

  const userMsgId = `user_${Date.now()}`;
  chatList.value.push({
    id: userMsgId,
    role: 'user',
    content: query,
  });

  inputText.value = '';
  loading.value = true;

  const botMsgId = `bot_${Date.now()}`;
  chatList.value.push({
    id: botMsgId,
    role: 'assistant',
    content: '正在思考与处理...',
    toolExpanded: [],
  });

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
          target.content = chunk;
          isFirstChunk = false;
        } else {
          target.content = chunk;
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
      if (target) {
        target.content = `❌ ${err}`;
      } else {
        MessagePlugin.error(err);
      }
    },
    () => {
      loading.value = false;
    }
  );
};
</script>

<template>
  <t-drawer
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
        <t-button variant="text" theme="danger" size="small" class="clear-btn" @click="handleClearHistory">
          <template #icon><t-icon name="delete" /></template>
          清空历史
        </t-button>
      </div>
    </template>

    <div class="ai-chat-container">
      <div class="chat-list-wrapper">
        <t-chat class="mslx-chat-component">
          <t-chat-item
            v-for="msg in chatList"
            :key="msg.id"
            :role="msg.role"
            :name="msg.role === 'assistant' ? 'MSLX AI' : (userStore.userInfo.name || userStore.userInfo.username || '用户')"
            :avatar="msg.role === 'assistant' ? 'https://www.mslmc.cn/logo.png' : (userStore.userInfo.avatar || 'https://tdesign.gtimg.com/site/avatar.jpg')"
          >
            <template #content>
              <!-- 思考状态动效 -->
              <div v-if="msg.content === '正在思考与处理...'" class="thinking-state">
                <t-loading size="small" text="AI 正在思考并处理中..." />
              </div>

              <!-- 用户消息文本 -->
              <div v-else-if="msg.role === 'user'" class="chat-bubble-text">
                {{ msg.content }}
              </div>

              <!-- AI 回复 Markdown -->
              <div v-else class="md-preview-wrapper">
                <md-preview
                  :editor-id="`ai-msg-${msg.id}`"
                  :model-value="msg.content"
                  :theme="mdTheme"
                  style="background: transparent; padding: 0"
                />
              </div>

              <!-- 高风险敏感操作授权确认卡片（独占呈现，极简质感） -->
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
                  <t-button theme="danger" size="small" class="action-btn" @click="handleConfirmToolAction(msg)">
                    <template #icon><t-icon name="check-circle" /></template>
                    确认授权{{ msg.toolData.data.action === 'delete' ? '删除' : '写入' }}
                  </t-button>
                  <t-button theme="default" variant="outline" size="small" class="action-btn" @click="handleRejectToolAction(msg)">
                    <template #icon><t-icon name="close-circle" /></template>
                    拒绝操作
                  </t-button>
                </div>
              </div>

              <!-- 操作已确认/已拒绝的状态显示 -->
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

              <!-- 工具调用细节折叠（纯调试用，默认收起） -->
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
          </t-chat-item>
        </t-chat>
      </div>

      <!-- AI 推荐快捷回复卡片（始终位于输入框正上方） -->
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
        <t-input
          v-model="inputText"
          placeholder="请输入指令，如“把 Java 切换成 25”、“重启服务器”..."
          :disabled="loading"
          size="large"
          @enter="handleSend"
        >
          <template #suffixIcon>
            <t-button v-if="loading" theme="danger" variant="base" size="small" @click="handleAbort">
              <template #icon><t-icon name="stop-circle" /></template>
              中断
            </t-button>
            <t-button v-else theme="primary" shape="square" @click="handleSend">
              <template #icon><t-icon name="send" /></template>
            </t-button>
          </template>
        </t-input>
      </div>
    </div>
  </t-drawer>
</template>

<style scoped>
.drawer-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding-right: 24px;
}

.drawer-title {
  display: flex;
  align-items: center;
}

.clear-btn {
  margin-left: auto;
}

.ai-chat-container {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.chat-list-wrapper {
  flex: 1;
  overflow-y: auto;
  padding: 12px 0;
}

.mslx-chat-component {
  width: 100%;
}

.chat-bubble-text {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.6;
}

.md-preview-wrapper {
  font-size: 14px;
}

.thinking-state {
  display: flex;
  align-items: center;
  padding: 6px 0;
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

.chat-input-area {
  padding-top: 12px;
  border-top: 1px solid var(--td-component-border);
}
</style>
