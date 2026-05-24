using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using System;
using System.Threading.Tasks;

namespace MSLX.Desktop.Services
{
    public class FrpTunnelSignalRService : IAsyncDisposable
    {
        #region 单例模式
        // 静态 Shared 供全局共享使用；
        // 需要多连接的场景直接 new FrpTunnelSignalRService() 即可。
        private static readonly Lazy<FrpTunnelSignalRService> _shared =
            new(() => new FrpTunnelSignalRService());

        public static FrpTunnelSignalRService Shared => _shared.Value;
        #endregion

        private HubConnection? _connection;

        #region 事件
        /// <summary>收到日志行：log</summary>
        public event Action<string>? LogReceived;

        /// <summary>连接状态变更</summary>
        public event Action<bool>? ConnectionStateChanged;
        #endregion

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        #region 连接管理

        /// <summary>
        /// 尝试连接 SignalR ，若已连接则直接返回，不会重复建立。
        /// </summary>
        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            // 旧连接存在但已断开，先清理
            if (_connection != null)
                await DisposeAsync();

            _connection = new HubConnectionBuilder()
                .WithUrl($"{ConfigStore.DaemonAddress}/api/hubs/frpLogsHub", options =>
                {
                    options.Headers.Add("x-api-key", ConfigStore.DaemonApiKey);
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>("ReceiveLog", (log) =>
            {
                LogReceived?.Invoke(log);
            });

            _connection.Reconnected += _ =>
            {
                ConnectionStateChanged?.Invoke(true);
                return Task.CompletedTask;
            };

            _connection.Closed += _ =>
            {
                ConnectionStateChanged?.Invoke(false);
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            ConnectionStateChanged?.Invoke(true);
        }

        #endregion

        #region Group 管理

        public async Task JoinGroupAsync(int tunnelId)
        {
            await ConnectAsync();
            await _connection!.InvokeAsync("JoinGroup", tunnelId);
        }

        public async Task LeaveGroupAsync(int tunnelId)
        {
            if (!IsConnected) return;
            await _connection!.InvokeAsync("LeaveGroup", tunnelId);
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                _connection.Remove("ReceiveLog");
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
    }
}