import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { request } from '@/utils/request';
import { TOKEN_NAME } from '@/config/global';
import { getHubUrl } from '@/utils/hub';

export interface AiSettingsModel {
  aiEnabled: boolean;
  aiApiKey: string;
  aiBaseUrl: string;
  aiModelName: string;
  aiSystemPrompt?: string;
}

export function getAiSettings() {
  return request.get<AiSettingsModel>({
    url: '/api/settings/ai',
  });
}

export function updateAiSettings(data: AiSettingsModel) {
  return request.post({
    url: '/api/settings/ai',
    data,
  });
}

export interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

let aiChatSignalRConn: HubConnection | null = null;
let activeHttpAbortController: AbortController | null = null;

export function abortAiChatStream() {
  if (activeHttpAbortController) {
    activeHttpAbortController.abort();
    activeHttpAbortController = null;
  }
  if (aiChatSignalRConn) {
    try {
      aiChatSignalRConn.stop();
    } catch (e) {
      console.warn('停止 SignalR 连接:', e);
    }
  }
}

export async function sendAiChatStream(
  messages: ChatMessage[],
  onMessage: (content: string) => void,
  onTool: (toolName: string, data: any) => void,
  onError: (error: string) => void,
  onDone: () => void
) {
  try {
    const hubUrl = getHubUrl('/api/hubs/aiChatHub');
    const token = localStorage.getItem(TOKEN_NAME) || '';

    if (!aiChatSignalRConn) {
      aiChatSignalRConn = new HubConnectionBuilder()
        .withUrl(hubUrl, {
          accessTokenFactory: () => token,
        })
        .withAutomaticReconnect()
        .build();
    }

    if (aiChatSignalRConn.state === 'Disconnected') {
      await aiChatSignalRConn.start();
    }

    aiChatSignalRConn.off('ChatChunk');
    aiChatSignalRConn.off('ToolExecuted');
    aiChatSignalRConn.off('ChatError');
    aiChatSignalRConn.off('ChatComplete');

    aiChatSignalRConn.on('ChatChunk', (chunk: string) => {
      onMessage(chunk);
    });

    aiChatSignalRConn.on('ToolExecuted', (toolName: string, data: any) => {
      onTool(toolName, data);
    });

    aiChatSignalRConn.on('ChatError', (err: string) => {
      onError(err);
      onDone();
    });

    aiChatSignalRConn.on('ChatComplete', () => {
      onDone();
    });

    await aiChatSignalRConn.invoke('SendMessage', messages);
  } catch (wsErr: any) {
    console.warn('SignalR WebSocket AI 对话尝试失败，回退至 HTTP SSE:', wsErr);
    await sendAiChatStreamHttp(messages, onMessage, onTool, onError, onDone);
  }
}

async function sendAiChatStreamHttp(
  messages: ChatMessage[],
  onMessage: (content: string) => void,
  onTool: (toolName: string, data: any) => void,
  onError: (error: string) => void,
  onDone: () => void
) {
  const token = localStorage.getItem(TOKEN_NAME) || '';
  const baseUrl = localStorage.getItem('BASE_URL_NAME') || '';
  const url = `${baseUrl}/api/ai/chat`;

  activeHttpAbortController = new AbortController();

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-user-token': token,
        Authorization: token ? `Bearer ${token}` : '',
      },
      signal: activeHttpAbortController.signal,
      body: JSON.stringify({ messages }),
    });

    if (!response.ok) {
      const errText = await response.text();
      onError(`请求失败 (${response.status}): ${errText}`);
      onDone();
      return;
    }

    const reader = response.body?.getReader();
    const decoder = new TextDecoder('utf-8');
    if (!reader) {
      onError('无法读取响应流');
      onDone();
      return;
    }

    let buffer = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const events = buffer.split('\n\n');
      buffer = events.pop() || '';

      for (const evt of events) {
        if (!evt.trim()) continue;
        const lines = evt.split('\n');
        let eventType = 'message';
        let eventData = '';

        for (const line of lines) {
          if (line.startsWith('event:')) {
            eventType = line.substring(6).trim();
          } else if (line.startsWith('data:')) {
            eventData = line.substring(5).trim();
          }
        }

        const unescapedData = eventData.replace(/\\n/g, '\n');

        if (eventType === 'message') {
          onMessage(unescapedData);
        } else if (eventType === 'tool_executed') {
          try {
            const parsed = JSON.parse(unescapedData);
            onTool(parsed.tool, parsed.data);
          } catch (e) {
            onTool('unknown', unescapedData);
          }
        } else if (eventType === 'error') {
          onError(unescapedData);
        } else if (eventType === 'done') {
          onDone();
          return;
        }
      }
    }

    onDone();
  } catch (err: any) {
    if (err.name !== 'AbortError') {
      onError(err.message || '网络连接失败');
    }
    onDone();
  } finally {
    activeHttpAbortController = null;
  }
}
