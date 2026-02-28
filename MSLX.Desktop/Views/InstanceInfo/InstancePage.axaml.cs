using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.Desktop.Services;
using MSLX.Desktop.Utils;
using SukiUI.Dialogs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.InstanceInfo;

public partial class InstancePage : UserControl
{
    private readonly InstanceSignalRService _signalR = new(); // SignalR 服务实例
    private int _instanceId; // 实例ID
    private bool _isRunning; // 状态
    public InstancePage() // 无参构造函数，供 XAML 设计器使用
    {
        InitializeComponent();
    }

    public InstancePage(int instanceID)
    {
        InitializeComponent();
        _instanceId = instanceID;
        this.Initialized += InstancePage_Initialized;
    }

    private async void InstancePage_Initialized(object? sender, System.EventArgs e)
    {
        await LoadAsync();
    }

    /// <summary>
    /// 加载实例页面，由外部（如导航）调用，传入实例ID
    /// </summary>
    public async Task LoadAsync()
    {
        ConsoleTab.AppendLog($"[实例] ID: {_instanceId}");
        await Task.Delay(500);
        // 1. 将发送指令的能力注入控制台 Tab
        ConsoleTab.SendCommandHandler = async cmd =>
            await _signalR.SendCommandAsync(_instanceId, cmd);

        // 2. 将设置保存结果回调注入设置 Tab
        SettingsTab.OnSaveResult = (msg, ok) =>
            ConsoleTab.AppendLog($"[设置] {msg}", ok ? LogLevel.Info : LogLevel.Error);

        // 3. 加载实例基本信息
        await RefreshInfoAsync();

        // 4. 加载设置 Tab 数据
        await SettingsTab.LoadAsync(_instanceId);

        // 5. 连接 SignalR，开始接收日志
        await ConnectSignalRAsync();
    }

    // 信息刷新
    private async Task RefreshInfoAsync()
    {
        var (success, info, msg) = await InstanceService.GetInstanceInfoAsync(_instanceId);
        if (!success || info == null)
        {
            InstanceNameText.Text = $"加载失败: {msg}";
            return;
        }

        var target = InstanceModel.Model.ServerList.FirstOrDefault(x => x.ID == _instanceId);
        if (target != null)
        {
            target.Status = info.Status;
            target.StatusText = info.StatusText;
        }

        InstanceNameText.Text = info.Name;
        StatusText.Text = info.StatusText;

        _isRunning = info.Status != 0;
        UpdateControlState();
    }

    #region SignalR 连接
    private async Task ConnectSignalRAsync()
    {
        _signalR.LogReceived += OnLogReceived;
        _signalR.ConnectionStateChanged += OnConnectionStateChanged;
        _signalR.EulaRequired += OnEulaRequired;

        try
        {
            await _signalR.ConnectAsync(_instanceId);
        }
        catch (Exception ex)
        {
            ConsoleTab.AppendLog($"[连接失败] {ex.Message}", LogLevel.Error);
        }
    }

    private void OnLogReceived(string log)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (log.StartsWith("[MSLX]"))
            {
                // 接收到MSLX日志时，查询一下当前实例状态，更新UI
                _ = RefreshInfoAsync();
            }

            var level = log.Contains("ERROR") ? LogLevel.Error
                      : log.Contains("WARN")  ? LogLevel.Warn
                      : log.Contains("INFO")  ? LogLevel.Info
                                               : LogLevel.Default;
            ConsoleTab.AppendLog(log, level);
        });
    }

    private void OnConnectionStateChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!connected)
                ConsoleTab.AppendLog("[SignalR 断开连接，尝试重连...]", LogLevel.Warn);
        });
    }
    #endregion

    #region EULA 处理
    private void OnEulaRequired()
    {
        // SignalR 回调在后台线程，必须切换到 UI 线程弹窗
        Dispatcher.UIThread.Post(() =>
        {
            ConsoleTab.AppendLog("[MSLX] 检测到 EULA 未签署，请在弹窗中确认是否同意。", LogLevel.Warn);

            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Warning)
                .WithTitle("Minecraft EULA 协议")
                .WithContent(
                    "服务器启动需要你同意 Minecraft 最终用户许可协议（EULA）。\n\n" +
                    "你可以在 https://aka.ms/MinecraftEULA 查看完整协议内容。\n\n" +
                    "是否同意并继续启动？")
                .WithActionButton("同意并启动", _ => AgreeEula(), true)
                .WithActionButton("拒绝", _ => { }, true)
                .TryShow();
        });
    }

    private async void AgreeEula()
    {
        var (ok, msg) = await InstanceService.SendInstanceActionAsync(
            _instanceId, "agreeEula", "true");

        if (ok)
        {
            ConsoleTab.AppendLog("[MSLX] 已同意 EULA，服务器将继续启动...", LogLevel.Info);
            await RefreshInfoAsync();
        }
        else
        {
            ConsoleTab.AppendLog($"[MSLX] EULA 操作失败: {msg}", LogLevel.Error);
        }
    }
    #endregion
    
    #region 按钮事件
    private async void OnControlClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ControlBtn.IsEnabled = false;
        try
        {
            if (_isRunning)
            {
                var (ok, msg) = await InstanceService.StopInstanceAsync(_instanceId);
                if (!ok)
                    ConsoleTab.AppendLog($"[停止失败] {msg}", LogLevel.Error);
            }
            else
            {
                var (ok, msg) = await InstanceService.StartInstanceAsync(_instanceId);
                if (!ok)
                    ConsoleTab.AppendLog($"[启动失败] {msg}", LogLevel.Error);
            }

            await Task.Delay(800);
            await RefreshInfoAsync();
        }
        finally
        {
            ControlBtn.IsEnabled = true;
        }
    }

    private async void OnBackupClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        BackupBtn.IsEnabled = false;
        try
        {
            var (ok, msg) = await InstanceService.BackupInstanceAsync(_instanceId);
            ConsoleTab.AppendLog(ok ? $"[备份已触发] {msg}" : $"[备份失败] {msg}",
                                 ok ? LogLevel.Info : LogLevel.Error);
        }
        finally
        {
            BackupBtn.IsEnabled = true;
        }
    }
    #endregion

    #region UI&其他

    // UI 同步
    private void UpdateControlState()
    {
        if (_isRunning)
        {
            StatusDot.Fill = new SolidColorBrush(Colors.LimeGreen);
            ControlBtn.Content = "停止";
            BackupBtn.IsEnabled = true;
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Colors.Gray);
            ControlBtn.Content = "启动";
            BackupBtn.IsEnabled = false;
        }
    }

    // 生命周期
    /// <summary>
    /// 页面卸载时释放 SignalR 连接（在导航离开时调用）
    /// </summary>
    public async Task UnloadAsync()
    {
        _signalR.LogReceived -= OnLogReceived;
        _signalR.ConnectionStateChanged -= OnConnectionStateChanged;
        _signalR.EulaRequired -= OnEulaRequired;
        await _signalR.LeaveGroupAsync(_instanceId);
        await _signalR.DisposeAsync();
    }
    #endregion
}
