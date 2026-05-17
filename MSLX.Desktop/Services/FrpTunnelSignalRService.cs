using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MSLX.Desktop.Services
{
    public class FrpTunnelSignalRService : IAsyncDisposable
    {
        private HubConnection? _connection;
        private int _currentTunnelID;

        #region 事件定义

        /// <summary>收到服务端日志行</summary>
        public event Action<int, string>? LogReceived;

        /// <summary>连接状态变更</summary>
        public event Action<bool>? ConnectionStateChanged;
        #endregion

        #region 属性
        public bool IsConnected => _connection?.State == HubConnectionState.Connected;
        #endregion

        #region SignalR
        /// <summary>
        /// 建立 SignalR 连接并加入指定实例组，开始接收日志
        /// </summary>
        public async Task ConnectAsync(int instanceId)
        {
            if (_connection != null)
                await DisposeAsync();
            _currentTunnelID = instanceId;
            _connection = new HubConnectionBuilder()
                .WithUrl($"{ConfigStore.DaemonAddress}/api/hubs/frpLogsHub", options =>
                {
                    options.Headers.Add("x-api-key", ConfigStore.DaemonApiKey);
                })
                .WithAutomaticReconnect()
                .Build();

            // 注册日志监听
            _connection.On<string>("ReceiveLog", log =>
            {
                LogReceived?.Invoke(_currentTunnelID, log);
            });

            // 连接状态回调
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
            await _connection.InvokeAsync("JoinGroup", instanceId);
            ConnectionStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 离开实例组（停止接收日志）
        /// </summary>
        public async Task LeaveGroupAsync(int instanceId)
        {
            if (_connection == null || !IsConnected) return;
            await _connection.InvokeAsync("LeaveGroup", instanceId);
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                _connection.Remove("ReceiveLog");
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        #endregion
    }
}
