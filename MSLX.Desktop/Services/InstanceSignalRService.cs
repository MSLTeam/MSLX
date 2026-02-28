using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using System;
using System.Threading.Tasks;

namespace MSLX.Desktop.Services;

/// <summary>
/// 封装 /api/hubs/instanceControlHub 的 SignalR 连接。
/// 每个实例页面持有一个独立实例，用完后务必调用 DisposeAsync()。
/// </summary>
public class InstanceSignalRService : IAsyncDisposable
{
    private HubConnection? _connection;

    #region 事件定义

    /// <summary>收到服务端日志行</summary>
    public event Action<string>? LogReceived;

    /// <summary>指令发送结果（不应显示在终端）</summary>
    public event Action<string>? CommandResultReceived;

    /// <summary>连接状态变更</summary>
    public event Action<bool>? ConnectionStateChanged;

    /// <summary>后端检测到 EULA 未同意，需要用户确认</summary>
    public event Action? EulaRequired;
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

        _connection = new HubConnectionBuilder()
            .WithUrl($"{ConfigStore.DaemonAddress}/api/hubs/instanceControlHub", options =>
            {
                options.Headers.Add("x-api-key", ConfigStore.DaemonApiKey);
            })
            .WithAutomaticReconnect()
            .Build();

        // 注册日志监听
        _connection.On<string>("ReceiveLog", log =>
        {
            LogReceived?.Invoke(log);
        });

        // 注册指令结果监听
        _connection.On<string>("CommandResult", result =>
        {
            CommandResultReceived?.Invoke(result);
        });

        // 注册 EULA 监听
        _connection.On("RequireEULA", () =>
        {
            EulaRequired?.Invoke();
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
    /// 发送指令到指定实例
    /// </summary>
    public async Task SendCommandAsync(int instanceId, string command)
    {
        if (_connection == null || !IsConnected)
            throw new InvalidOperationException("SignalR 未连接");

        await _connection.InvokeAsync("SendCommand", instanceId, command);
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
            _connection.Remove("CommandResult");
            _connection.Remove("RequireEULA");
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
    #endregion
}
