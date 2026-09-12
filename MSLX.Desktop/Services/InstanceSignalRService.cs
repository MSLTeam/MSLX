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
    /// <summary>后端检测到 EULA 未同意，需要用户确认</summary>
    public event Action? EulaRequired;

    /// <summary>收到 PTY 原始输出流块</summary>
    public event Action<string>? PtyDataReceived;

    /// <summary>收到 PTY 状态 (isPty, isRunning)</summary>
    public event Action<bool, bool>? PtyStatusReceived;
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

        // 注册 PTY 数据流监听
        _connection.On<string>("ReceivePtyData", chunk =>
        {
            PtyDataReceived?.Invoke(chunk);
        });

        // 注册 PTY 状态监听
        _connection.On<System.Text.Json.JsonElement>("PtyStatus", statusEl =>
        {
            bool isPty = statusEl.TryGetProperty("isPty", out var p1) && p1.GetBoolean();
            bool isRunning = statusEl.TryGetProperty("isRunning", out var p2) && p2.GetBoolean();
            bool enablePty = statusEl.TryGetProperty("enablePty", out var p3) && p3.GetBoolean();
            PtyStatusReceived?.Invoke(isPty || enablePty, isRunning);
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
        try
        {
            await _connection.InvokeAsync("GetPtyStatus", (uint)instanceId);
        }
        catch { }
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
    /// 加入 PTY 伪终端会话组
    /// </summary>
    public async Task JoinPtyGroupAsync(int instanceId, int cols, int rows)
    {
        if (_connection != null && IsConnected)
        {
            await _connection.InvokeAsync("JoinPtyGroup", (uint)instanceId, cols, rows);
        }
    }

    /// <summary>
    /// 离开 PTY 伪终端会话组
    /// </summary>
    public async Task LeavePtyGroupAsync(int instanceId)
    {
        if (_connection != null && IsConnected)
        {
            await _connection.InvokeAsync("LeavePtyGroup", (uint)instanceId);
        }
    }

    /// <summary>
    /// 查询 PTY 状态
    /// </summary>
    public async Task GetPtyStatusAsync(int instanceId)
    {
        if (_connection != null && IsConnected)
        {
            await _connection.InvokeAsync("GetPtyStatus", (uint)instanceId);
        }
    }

    /// <summary>
    /// 发送 PTY 原始输入数据（键盘事件、转义字符等）
    /// </summary>
    public async Task SendPtyInputAsync(int instanceId, string data)
    {
        if (_connection != null && IsConnected)
        {
            await _connection.InvokeAsync("SendPtyInput", (uint)instanceId, data);
        }
    }

    /// <summary>
    /// 调整 PTY 伪终端行列尺寸
    /// </summary>
    public async Task ResizePtyAsync(int instanceId, int cols, int rows)
    {
        if (_connection != null && IsConnected)
        {
            await _connection.InvokeAsync("ResizePty", (uint)instanceId, cols, rows);
        }
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
