import { defineStore } from 'pinia';
import { ref } from 'vue';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useUserStore } from '@/store';

export const useInstanceHubStore = defineStore('instanceHub', () => {
  const userStore = useUserStore();

  const connection = ref<HubConnection | null>(null);
  const isConnected = ref(false);
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
  const commandResultHandlers = new Set<(_success: boolean, _msg: string) => void>();


  // 随时更新最大内存，无需重连
  const setMaxMemory = (mb: number) => {
    currentMaxMemory.value = mb;
  };

  // connect 不再需要传 maxMemory
  const connect = (serverId: number) => {
    connectionPromise = connectionPromise.then(async () => {
      refCount.value++;

      if (connection.value?.state === 'Connected' && currentServerId.value === serverId) {
        return;
      }

      if (connection.value) {
        await _stopInternal();
      }

      currentServerId.value = serverId;
      currentMaxMemory.value = 0;

      const { baseUrl, token } = userStore;
      const hubUrl = new URL('/api/hubs/instanceControlHub', baseUrl || window.location.origin);
      if (token) hubUrl.searchParams.append('x-user-token', token);

      const newConnection = new HubConnectionBuilder()
        .withUrl(hubUrl.toString())
        .configureLogging(LogLevel.Warning)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

      // --- 注册事件 ---
      newConnection.on('ReceiveLog', (message: string) => {
        logHandlers.forEach(handler => handler(message));
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
          memPercent // 没最大内存 0
        };
      });

      newConnection.onreconnecting(() => logHandlers.forEach(h => h('\x1b[1;31m[System] 连接中断，尝试重连...\x1b[0m')));

      newConnection.onreconnected(async () => {
        logHandlers.forEach(h => h('\x1b[1;32m[System] 网络恢复，重新加入会话...\x1b[0m'));
        try { await newConnection.invoke('JoinGroup', serverId); } catch (e) { console.error(e); }
      });

      try {
        await newConnection.start();
        await newConnection.invoke('JoinGroup', serverId);
        connection.value = newConnection;
        isConnected.value = true;
        logHandlers.forEach(h => h('\x1b[1;32m[System] 已连接到实例控制服务\x1b[0m'));
      } catch (err: any) {
        isConnected.value = false;
        logHandlers.forEach(h => h(`\x1b[1;31m[Error] 连接失败: ${err.message}\x1b[0m`));
        currentServerId.value = null;
        connection.value = null;
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
    if (connection.value) {
      try {
        if (connection.value.state === 'Connected' && currentServerId.value) {
          await connection.value.invoke('LeaveGroup', currentServerId.value);
        }
        await connection.value.stop();
      } catch (e) { console.warn(e); }
    }
    connection.value = null;
    isConnected.value = false;
    currentServerId.value = null;
    currentMaxMemory.value = 0; // 重置
    stats.value = { cpu: 0, memBytes: 0, memPercent: 0 };
  };

  const sendCommand = async (cmd: string) => {
    if (!connection.value || connection.value.state !== 'Connected' || !currentServerId.value) {
      throw new Error('未连接到服务');
    }
    await connection.value.invoke('SendCommand', currentServerId.value, cmd);
  };

  const onLog = (handler: (_msg: string) => void) => {
    logHandlers.add(handler);
    return () => logHandlers.delete(handler);
  };

  const onCommandResult = (handler: (_success: boolean, _msg: string) => void) => {
    commandResultHandlers.add(handler);
    return () => commandResultHandlers.delete(handler);
  };

  return {
    isConnected,
    stats,
    currentServerId,
    connect,
    disconnect,
    setMaxMemory,
    sendCommand,
    onLog,
    onCommandResult
  };
});
