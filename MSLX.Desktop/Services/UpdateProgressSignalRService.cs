using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using System;
using System.Threading.Tasks;

namespace MSLX.Desktop.Services;

/// <summary>
/// 封装 /api/hubs/updateProgressHub 的 SignalR 连接，用于监听实例更新进度。
/// </summary>
public class UpdateProgressSignalRService : IAsyncDisposable
{
    private HubConnection? _connection;

    #region 事件
    /// <summary>
    /// 收到更新状态推送
    /// </summary>
    /// <param name="msg">消息文本</param>
    /// <param name="progress">进度（0~100）</param>
    /// <param name="isError">是否为错误</param>
    public event Action<string, double, bool>? UpdateStatusReceived;

    /// <summary>连接状态变更</summary>
    public event Action<bool>? ConnectionStateChanged;
    #endregion

    #region 属性
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
    #endregion

    #region SignalR
    /// <summary>
    /// 建立连接并订阅指定实例的更新进度
    /// </summary>
    public async Task ConnectAsync(int instanceId)
    {
        if (_connection != null)
            await DisposeAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{ConfigStore.DaemonAddress}/api/hubs/updateProgressHub", options =>
            {
                options.Headers.Add("x-api-key", ConfigStore.DaemonApiKey);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, double, bool>("UpdateStatus", (msg, prog, isErr) =>
        {
            UpdateStatusReceived?.Invoke(msg, prog, isErr);
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
        await _connection.InvokeAsync("JoinGroup", instanceId);
        ConnectionStateChanged?.Invoke(true);
    }

    /// <summary>
    /// 取消订阅
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
            _connection.Remove("UpdateStatus");
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
    #endregion
}
