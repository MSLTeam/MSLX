using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MSLX.Desktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.InstanceInfo;

/// <summary>日志条目</summary>
public record LogEntry(string Text, IBrush Color);

/// <summary>日志级别</summary>
public enum LogLevel { Default, Info, Warn, Error }

public partial class InstanceConsoleTab : UserControl
{
    // ==================== 黑夜模式 (黑毛玻璃) 笔刷体系 ====================
    private static readonly IBrush DarkContainerBg = Brush.Parse("#99141418");
    private static readonly IBrush DarkContainerBorder = Brush.Parse("#25ffffff");
    private static readonly IBrush DarkHeaderBg = Brush.Parse("#1affffff");
    private static readonly IBrush DarkHeaderBorder = Brush.Parse("#15ffffff");
    private static readonly IBrush DarkTitleFg = Brush.Parse("#e4e4e7");
    private static readonly IBrush DarkBadgeBg = Brush.Parse("#3306b6d4");
    private static readonly IBrush DarkBadgeFg = Brush.Parse("#22d3ee");
    private static readonly IBrush DarkTrackBg = Brush.Parse("#33000000");
    private static readonly IBrush DarkActiveBtnBg = Brush.Parse("#40ffffff");
    private static readonly IBrush DarkActiveBtnFg = Brushes.White;
    private static readonly IBrush DarkInactiveBtnFg = Brush.Parse("#a1a1aa");
    private static readonly IBrush DarkTerminalFg = Brush.Parse("#f4f4f5");
    private static readonly Color DarkCursorColor = Color.Parse("#22d3ee");
    private static readonly IBrush DarkInputBg = Brush.Parse("#33000000");
    private static readonly IBrush DarkInputBorder = Brush.Parse("#25ffffff");
    private static readonly IBrush DarkInputFg = Brush.Parse("#f4f4f5");

    // ==================== 白天模式 (白毛玻璃) 笔刷体系 ====================
    private static readonly IBrush LightContainerBg = Brush.Parse("#C8FFFFFF");
    private static readonly IBrush LightContainerBorder = Brush.Parse("#60CBD5E1");
    private static readonly IBrush LightHeaderBg = Brush.Parse("#40FFFFFF");
    private static readonly IBrush LightHeaderBorder = Brush.Parse("#25CBD5E1");
    private static readonly IBrush LightTitleFg = Brush.Parse("#27272a");
    private static readonly IBrush LightBadgeBg = Brush.Parse("#E0F2FE");
    private static readonly IBrush LightBadgeFg = Brush.Parse("#0284c7");
    private static readonly IBrush LightTrackBg = Brush.Parse("#18000000");
    private static readonly IBrush LightActiveBtnBg = Brushes.White;
    private static readonly IBrush LightActiveBtnFg = Brush.Parse("#18181b");
    private static readonly IBrush LightInactiveBtnFg = Brush.Parse("#71717a");
    private static readonly IBrush LightTerminalFg = Brush.Parse("#18181b");
    private static readonly Color LightCursorColor = Color.Parse("#0284c7");
    private static readonly IBrush LightInputBg = Brush.Parse("#18000000");
    private static readonly IBrush LightInputBorder = Brush.Parse("#30000000");
    private static readonly IBrush LightInputFg = Brush.Parse("#18181b");

    private static readonly IBrush InactiveBtnBg = Brushes.Transparent;

    private readonly ObservableCollection<LogEntry> _logs = new();
    private RemotePtyConnection? _remotePty;
    private InstanceSignalRService? _signalR;
    private int _instanceId;
    private bool _isPtyAttached;
    private bool _isPtyJoined;
    private bool _isPtyConfigured;
    private bool _userManuallySwitched;
    private bool _isPtyMode;
    private bool _isCurrentDark;

    /// <summary>由 InstancePage 注入，用于发送指令</summary>
    public Func<string, Task>? SendCommandHandler { get; set; }

    public InstanceConsoleTab()
    {
        InitializeComponent();
        LogList.ItemsSource = _logs;
        UpdateTabButtons(isPty: false);

        // 初始化当前主题模式（白天白毛玻璃 / 黑夜黑毛玻璃）
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        ApplyTheme(isDark);

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }
        if (_signalR != null)
        {
            _signalR.ConnectionStateChanged -= OnConnectionStateChanged;
            _signalR.PtyStatusReceived -= OnPtyStatusReceived;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            ApplyTheme(isDark);
        });
    }

    private void ApplyTheme(bool isDark)
    {
        _isCurrentDark = isDark;

        ConsoleContainer.Background = isDark ? DarkContainerBg : LightContainerBg;
        ConsoleContainer.BorderBrush = isDark ? DarkContainerBorder : LightContainerBorder;

        HeaderBorder.Background = isDark ? DarkHeaderBg : LightHeaderBg;
        HeaderBorder.BorderBrush = isDark ? DarkHeaderBorder : LightHeaderBorder;

        ConsoleTitleText.Foreground = isDark ? DarkTitleFg : LightTitleFg;
        PtyBadge.Background = isDark ? DarkBadgeBg : LightBadgeBg;
        PtyBadgeText.Foreground = isDark ? DarkBadgeFg : LightBadgeFg;
        PtyHintText.Foreground = isDark ? DarkInactiveBtnFg : LightInactiveBtnFg;

        ModeTrackBorder.Background = isDark ? DarkTrackBg : LightTrackBg;

        Terminal.Foreground = isDark ? DarkTerminalFg : LightTerminalFg;
        Terminal.CursorColor = isDark ? DarkCursorColor : LightCursorColor;

        CmdInput.Background = isDark ? DarkInputBg : LightInputBg;
        CmdInput.BorderBrush = isDark ? DarkInputBorder : LightInputBorder;
        CmdInput.Foreground = isDark ? DarkInputFg : LightInputFg;

        UpdateTabButtons(_isPtyMode);
    }

    public void SetInstanceName(string name, int instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ConsoleTitleText.Text = $"MSLX 控制台 | #{name}";
        });
    }

    public void SetPtyConfigured(bool enabled)
    {
        _isPtyConfigured = enabled;
        Dispatcher.UIThread.Post(async () =>
        {
            PtyBadge.IsVisible = enabled;
            PtyHintText.IsVisible = !enabled;
            if (enabled && !_userManuallySwitched && !_isPtyMode)
            {
                await SwitchToPtyAsync(isManual: false);
            }
        });
    }

    public void InitPty(InstanceSignalRService signalR, int instanceId)
    {
        _signalR = signalR;
        _instanceId = instanceId;

        _signalR.ConnectionStateChanged += OnConnectionStateChanged;
        _signalR.PtyStatusReceived += OnPtyStatusReceived;
    }

    private void OnConnectionStateChanged(bool connected)
    {
        if (connected && _isPtyMode && !_isPtyJoined)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await EnsurePtyJoinedAsync();
            });
        }
    }

    private void OnPtyStatusReceived(bool isPtyOrConfigured, bool isRunning)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (isPtyOrConfigured)
            {
                _isPtyConfigured = true;
                PtyBadge.IsVisible = true;
                PtyHintText.IsVisible = false;
                if (!_userManuallySwitched)
                {
                    await SwitchToPtyAsync(isManual: false);
                }
                else if (_isPtyMode && !_isPtyJoined)
                {
                    await EnsurePtyJoinedAsync();
                }
            }
        });
    }

    private void UpdateTabButtons(bool isPty)
    {
        _isPtyMode = isPty;
        LogViewPanel.IsVisible = !isPty;
        PtyViewPanel.IsVisible = isPty;

        var activeBg = _isCurrentDark ? DarkActiveBtnBg : LightActiveBtnBg;
        var activeFg = _isCurrentDark ? DarkActiveBtnFg : LightActiveBtnFg;
        var inactiveFg = _isCurrentDark ? DarkInactiveBtnFg : LightInactiveBtnFg;

        PtyModeButton.Background = isPty ? activeBg : InactiveBtnBg;
        LogModeButton.Background = !isPty ? activeBg : InactiveBtnBg;

        PtyIcon.Foreground = isPty ? activeFg : inactiveFg;
        PtyText.Foreground = isPty ? activeFg : inactiveFg;
        PtyText.FontWeight = isPty ? FontWeight.SemiBold : FontWeight.Normal;

        LogIcon.Foreground = !isPty ? activeFg : inactiveFg;
        LogText.Foreground = !isPty ? activeFg : inactiveFg;
        LogText.FontWeight = !isPty ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private async void OnLogModeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _userManuallySwitched = true;
        UpdateTabButtons(isPty: false);
        _isPtyJoined = false;
        if (_signalR != null && _instanceId > 0 && _signalR.IsConnected)
        {
            await _signalR.LeavePtyGroupAsync(_instanceId);
        }
    }

    private async void OnPtyModeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await SwitchToPtyAsync(isManual: true);

    private async Task EnsurePtyJoinedAsync()
    {
        if (_signalR != null && _signalR.IsConnected && _instanceId > 0 && !_isPtyJoined)
        {
            _isPtyJoined = true;
            await _signalR.JoinPtyGroupAsync(_instanceId, 80, 24);
        }
    }

    private async Task SwitchToPtyAsync(bool isManual = false)
    {
        if (isManual) _userManuallySwitched = true;
        UpdateTabButtons(isPty: true);

        if (_signalR != null && _instanceId > 0)
        {
            if (!_isPtyAttached)
            {
                try
                {
                    _remotePty = new RemotePtyConnection(_signalR, _instanceId);
                    Terminal.AttachConnection(_remotePty);
                    _isPtyAttached = true;
                }
                catch (Exception ex)
                {
                    AppendLog($"[PTY 挂载失败] {ex.Message}", LogLevel.Error);
                }
            }

            await EnsurePtyJoinedAsync();
            Terminal.Focus();
        }
    }

    #region 公共方法
    /// <summary>
    /// 向控制台追加一行日志
    /// </summary>
    public void AppendLog(string text, LogLevel level = LogLevel.Default)
    {
        // 允许从任意线程调用
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(text, level));
            return;
        }

        var brush = level switch
        {
            LogLevel.Info    => _isCurrentDark ? Brush.Parse("#4ade80") : Brush.Parse("#16a34a"),
            LogLevel.Warn    => _isCurrentDark ? Brush.Parse("#fbbf24") : Brush.Parse("#d97706"),
            LogLevel.Error   => _isCurrentDark ? Brush.Parse("#f87171") : Brush.Parse("#dc2626"),
            _                => _isCurrentDark ? Brush.Parse("#93c5fd") : Brush.Parse("#0284c7")
        };

        _logs.Add(new LogEntry(text, brush));

        // 自动滚动到底部
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>清空日志</summary>
    public void ClearLogs() => _logs.Clear();
    #endregion

    #region 事件处理
    private async void OnSendCmdClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await TrySendCommand();

    private async void OnCmdInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await TrySendCommand();
    }
    #endregion

    #region 私有方法
    private async Task TrySendCommand()
    {
        var cmd = CmdInput.Text?.Trim();
        if (string.IsNullOrEmpty(cmd)) return;

        CmdInput.Text = string.Empty;
        try
        {
            if (SendCommandHandler != null)
                await SendCommandHandler(cmd);
        }
        catch (Exception ex)
        {
            AppendLog($"[发送失败] {ex.Message}", LogLevel.Error);
        }
    }
    #endregion
}
