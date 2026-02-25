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
  const eulaHandlers = new Set<() => void>();
  const commandResultHandlers = new Set<(_success: boolean, _msg: string) => void>();

  // 玩家状态管理
  const playerJoinedHandlers = new Set<(_name: string) => void>();
  const playerLeftHandlers = new Set<(_name: string) => void>();
  const playerListClearedHandlers = new Set<() => void>();


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
        .withUrl(hubUrl.toString(),{ withCredentials: false })
        .configureLogging(LogLevel.Warning)
        .withAutomaticReconnect([0, 2000, 5000, 10000])
        .build();

      // --- 注册事件 ---
      newConnection.on('ReceiveLog', (message: string) => {
        logHandlers.forEach((handler) => handler(message));
      });

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

  const onEula = (handler: () => void) => {
    eulaHandlers.add(handler);
    return () => logHandlers.delete(handler);
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

  return {
    isConnected,
    stats,
    currentServerId,
    connect,
    disconnect,
    setMaxMemory,
    sendCommand,
    onLog,
    onEula,
    onCommandResult,
    onPlayerJoined,
    onPlayerLeft,
    onPlayerListCleared,
  };
});
