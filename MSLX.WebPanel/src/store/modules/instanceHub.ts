import { defineStore } from 'pinia';
import { ref, shallowRef } from 'vue';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getHubUrl, getHubOptions } from '@/utils/hub';
import { getPageNodeId } from '@/utils/nodeTarget';
import { ConsoleStream, type ConsoleCursor, type ConsoleReplay } from '@/utils/consoleStream';

export const useInstanceHubStore = defineStore('instanceHub', () => {
  const connection = shallowRef<HubConnection | null>(null);
  const isConnected = ref(false);
  const connectionNotice = ref('');
  let noticeTimer: ReturnType<typeof setTimeout> | undefined;
  let wantsPty = false;
  let ptySize = { cols: 80, rows: 24 };
  let modeVersion = 0;
  let reconnectAbort: AbortController | null = null;
  let restoring = false;
  let restorePromise: Promise<void> | null = null;
  let buffered: { pty: boolean; text: string; cursor: ConsoleCursor }[] = [];
  let currentNodeId = 'local';
  const currentServerId = ref<number | null>(null);
  const refCount = ref(0);
  const currentMaxMemory = ref(0);

  // 操作锁
  let connectionPromise: Promise<void> = Promise.resolve();

  // 状态数据
  const stats = ref({
    cpu: 0,
    memBytes: 0,
    memPercent: 0
  });

  const logHandlers = new Set<(_msg: string) => void>();
  const eulaHandlers = new Set<() => void>();
  const commandResultHandlers = new Set<(_success: boolean, _msg: string) => void>();

  // 玩家状态管理
  const playerJoinedHandlers = new Set<(_name: string) => void>();
  const playerLeftHandlers = new Set<(_name: string) => void>();
  const playerListClearedHandlers = new Set<() => void>();

  // PTY 终端会话管理
  const ptyDataHandlers = new Set<(_chunk: string) => void>();
  const ptyStatusHandlers = new Set<(_status: { isPty: boolean; isRunning: boolean }) => void>();


  const logStream = new ConsoleStream(text => logHandlers.forEach(handler => handler(text)));
  const ptyStream = new ConsoleStream(text => ptyDataHandlers.forEach(handler => handler(text)));
  const markInterrupted = () => {
    isConnected.value = false;
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => { connectionNotice.value = '正在恢复连接…'; }, 2000);
  };
  const requireConnected = () => {
    if (isConnected.value) return true;
    connectionNotice.value = '连接尚未恢复，输入未发送';
    return false;
  };

  const restoreSession = (hub = connection.value): Promise<void> => {
    if (!hub || hub.state !== 'Connected') return Promise.resolve();
    if (restorePromise) return restorePromise;
    restoring = true;
    isConnected.value = false;
    const task = (async () => {
      let version: number;
      let gap = false;
      do {
        version = modeVersion;
        const result = await hub.invoke<{ log: ConsoleReplay | null; pty: ConsoleReplay | null; status: { isPty: boolean; isRunning: boolean } }>(
          'ResumeConsole', currentServerId.value, logStream.cursor, ptyStream.cursor, wantsPty, ptySize.cols, ptySize.rows);
        if (connection.value !== hub) return;
        gap ||= !!result.log?.gap || !!result.pty?.gap;
        logStream.replay(result.log);
        if (wantsPty) ptyStream.replay(result.pty);
        ptyStatusHandlers.forEach(handler => handler(result.status));
        const pending = buffered;
        buffered = [];
        for (const frame of pending) {
          if (!frame.pty || wantsPty) (frame.pty ? ptyStream : logStream).receive(frame.text, frame.cursor);
        }
      } while (version !== modeVersion);
      if (connection.value !== hub) return;
      clearTimeout(noticeTimer);
      connectionNotice.value = gap ? '部分输出超出缓冲或进程已更换，历史可能不完整' : '';
      isConnected.value = true;
    })();
    restorePromise = task;
    return task.finally(() => {
      if (restorePromise === task) { restoring = false; restorePromise = null; }
    });
  };

  const receiveFrame = (pty: boolean, text: string, cursor?: ConsoleCursor) => {
    if (!cursor) return;
    const stream = pty ? ptyStream : logStream;
    if (restoring) { buffered.push({ pty, text, cursor }); return; }
    if (!stream.receive(text, cursor)) {
      buffered.push({ pty, text, cursor });
      void restoreSession().catch(() => { connectionNotice.value = '恢复会话失败，请刷新页面'; });
    }
  };

  // 系统生命周期/控制台消息通知
  const systemMessageHandlers = new Set<(_msg: string) => void>();

  const emitSystemMsg = (msg: string) => {
    systemMessageHandlers.forEach((handler) => handler(msg));
  };

  // 随时更新最大内存，无需重连
  const setMaxMemory = (mb: number) => {
    currentMaxMemory.value = mb;
  };

  // connect 不再需要传 maxMemory
  const connect = (serverId: number, nodeId = getPageNodeId()) => {
    connectionPromise = connectionPromise.catch(() => {}).then(async () => {
      refCount.value++;

      if (connection.value?.state === 'Connected' && currentServerId.value === serverId && currentNodeId === nodeId) {
        return;
      }

      if (connection.value) {
        await _stopInternal();
      }

      currentServerId.value = serverId;
      currentNodeId = nodeId;
      currentMaxMemory.value = 0;

      const hubUrlStr = getHubUrl('/api/hubs/instanceControlHub', nodeId);

      const newConnection = new HubConnectionBuilder()
        .withUrl(hubUrlStr, getHubOptions(hubUrlStr))
        .configureLogging(LogLevel.Warning)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

      // --- 注册事件 ---
      newConnection.on('ReceiveLog', (message: string, cursor?: ConsoleCursor) => receiveFrame(false, message, cursor));

      newConnection.on('RequireEULA', () => {
        eulaHandlers.forEach((handler) => handler());
      });

      newConnection.on('CommandResult', (success: boolean, msg: string) => {
        commandResultHandlers.forEach(handler => handler(success, msg));
      });

      newConnection.on('ReceiveStatus', (id: number | string, cpu: number, memBytes: number) => {
        if (String(id) !== String(serverId)) return;

        let memPercent = 0;
        if (currentMaxMemory.value > 0) {
          const maxBytes = currentMaxMemory.value * 1024 * 1024;
          memPercent = (memBytes / maxBytes) * 100;
          if (memPercent > 100) memPercent = 100;
        }

        stats.value = {
          cpu,
          memBytes,
          memPercent, // 没最大内存 0
        };
      });

      // 玩家事件
      newConnection.on('PlayerJoined', (id: number | string, name: string) => {
        if (String(id) === String(serverId)) {
          playerJoinedHandlers.forEach((handler) => handler(name));
        }
      });

      newConnection.on('PlayerLeft', (id: number | string, name: string) => {
        if (String(id) === String(serverId)) {
          playerLeftHandlers.forEach((handler) => handler(name));
        }
      });

      newConnection.on('PlayerListCleared', (id: number | string) => {
        if (String(id) === String(serverId)) {
          playerListClearedHandlers.forEach((handler) => handler());
        }
      });

      newConnection.on('ReceivePtyData', (chunk: string, cursor?: ConsoleCursor) => receiveFrame(true, chunk, cursor));

      newConnection.on('PtyStatus', (status: { isPty: boolean; isRunning: boolean }) => {
        ptyStatusHandlers.forEach((handler) => handler(status));
      });

      let automaticReconnecting = false;
      let restarting = false;
      newConnection.onreconnecting(() => {
        if (connection.value !== newConnection) return;
        automaticReconnecting = true;
        markInterrupted();
      });
      newConnection.onreconnected(async () => {
        if (connection.value !== newConnection) return;
        automaticReconnecting = false;
        try { await restoreSession(newConnection); }
        catch { connectionNotice.value = '恢复会话失败，请刷新页面'; }
      });
      newConnection.onclose(async () => {
        if (connection.value !== newConnection || restarting) return;
        // 服务器侧关闭，不进入重连
        if (!automaticReconnecting) {
          restarting = true;
          markInterrupted();
          const abort = new AbortController();
          reconnectAbort = abort;
          try {
            for (const delay of [0, 2000, 5000, 10000]) {
              if (delay) await new Promise<void>(resolve => {
                const timer = setTimeout(resolve, delay);
                abort.signal.addEventListener('abort', () => { clearTimeout(timer); resolve(); }, { once: true });
              });
              if (abort.signal.aborted || connection.value !== newConnection) return;
              try {
                await newConnection.start();
                await restoreSession(newConnection);
                return;
              } catch {
                await newConnection.stop();
              }
            }
          } finally {
            restarting = false;
            if (reconnectAbort === abort) reconnectAbort = null;
          }
        }
        if (connection.value !== newConnection) return;
        clearTimeout(noticeTimer);
        isConnected.value = false;
        connectionNotice.value = '连接已断开，请刷新页面';
      });

      try {
        await newConnection.start();
        connection.value = newConnection;
        await restoreSession(newConnection);
      } catch (err: any) {
        isConnected.value = false;
        emitSystemMsg(`\x1b[1;31m[Error] 连接失败: ${err.message}\x1b[0m`);
        currentServerId.value = null;
        connection.value = null;
        connectionNotice.value = '连接失败，请刷新页面重试';
        await newConnection.stop();
      }
    });
    return connectionPromise;
  };

  const disconnect = () => {
    connectionPromise = connectionPromise.then(async () => {
      if (refCount.value > 0) refCount.value--;
      if (refCount.value === 0) {
        await _stopInternal();
      }
    });
    return connectionPromise;
  };

  const _stopInternal = async () => {
    const stopping = connection.value;
    connection.value = null;
    reconnectAbort?.abort();
    reconnectAbort = null;
    if (stopping) {
      try {
        if (stopping.state === 'Connected' && currentServerId.value) {
          await stopping.invoke('LeaveGroup', currentServerId.value);
        }
        await stopping.stop();
      } catch (e) { console.warn(e); }
    }
    clearTimeout(noticeTimer);
    connectionNotice.value = '';
    wantsPty = false;
    modeVersion++;
    restorePromise = null;
    restoring = false;
    buffered = [];
    logStream.reset();
    ptyStream.reset();
    isConnected.value = false;
    currentServerId.value = null;
    currentMaxMemory.value = 0; // 重置
    stats.value = { cpu: 0, memBytes: 0, memPercent: 0 };
  };

  const sendCommand = async (cmd: string) => {
    if (!requireConnected() || !connection.value || connection.value.state !== 'Connected' || !currentServerId.value) {
      throw new Error('连接尚未恢复，指令未发送');
    }
    await connection.value.invoke('SendCommand', currentServerId.value, cmd);
  };

  const onLog = (handler: (_msg: string) => void) => {
    logHandlers.add(handler);
    return () => logHandlers.delete(handler);
  };

  const onSystemMessage = (handler: (_msg: string) => void) => {
    systemMessageHandlers.add(handler);
    return () => systemMessageHandlers.delete(handler);
  };

  const onEula = (handler: () => void) => {
    eulaHandlers.add(handler);
    return () => eulaHandlers.delete(handler);
  };

  const onCommandResult = (handler: (_success: boolean, _msg: string) => void) => {
    commandResultHandlers.add(handler);
    return () => commandResultHandlers.delete(handler);
  };

  // 玩家事件
  const onPlayerJoined = (handler: (_name: string) => void) => {
    playerJoinedHandlers.add(handler);
    return () => playerJoinedHandlers.delete(handler);
  };

  const onPlayerLeft = (handler: (_name: string) => void) => {
    playerLeftHandlers.add(handler);
    return () => playerLeftHandlers.delete(handler);
  };

  const onPlayerListCleared = (handler: () => void) => {
    playerListClearedHandlers.add(handler);
    return () => playerListClearedHandlers.delete(handler);
  };

  const joinPtyGroup = async (cols: number, rows: number) => {
    wantsPty = true;
    ptySize = { cols, rows };
    modeVersion++;
    await restoreSession();
  };

  const leavePtyGroup = async () => {
    wantsPty = false;
    modeVersion++;
    await restoreSession();
  };

  const sendPtyInput = async (data: string) => {
    if (!requireConnected() || !connection.value || !currentServerId.value) return;
    try { await connection.value.invoke('SendPtyInput', currentServerId.value, data); }
    catch { connectionNotice.value = '输入发送失败，请确认后重试'; }
  };

  const resizePty = async (cols: number, rows: number) => {
    if (cols <= 0 || rows <= 0) return;
    if (ptySize.cols !== cols || ptySize.rows !== rows) modeVersion++;
    ptySize = { cols, rows };
    if (!isConnected.value || !connection.value || !currentServerId.value) return;
    try { await connection.value.invoke('ResizePty', currentServerId.value, cols, rows); }
    catch (e) { console.error('[PTY] ResizePty failed:', e); }
  };

  const getPtyStatus = async () => {
    if (!connection.value || connection.value.state !== 'Connected' || !currentServerId.value) return;
    try {
      await connection.value.invoke('GetPtyStatus', currentServerId.value);
    } catch (e) {
      console.error('[PTY] GetPtyStatus failed:', e);
    }
  };

  const onPtyData = (handler: (_chunk: string) => void) => {
    ptyDataHandlers.add(handler);
    return () => ptyDataHandlers.delete(handler);
  };

  const onPtyStatus = (handler: (_status: { isPty: boolean; isRunning: boolean }) => void) => {
    ptyStatusHandlers.add(handler);
    return () => ptyStatusHandlers.delete(handler);
  };

  return {
    isConnected,
    connectionNotice,
    requireConnected,
    stats,
    currentServerId,
    connect,
    disconnect,
    setMaxMemory,
    sendCommand,
    onLog,
    onSystemMessage,
    onEula,
    onCommandResult,
    onPlayerJoined,
    onPlayerLeft,
    onPlayerListCleared,
    joinPtyGroup,
    leavePtyGroup,
    sendPtyInput,
    resizePty,
    getPtyStatus,
    onPtyData,
    onPtyStatus,
  };
});
