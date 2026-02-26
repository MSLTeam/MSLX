using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly ObservableCollection<LogEntry> _logs = new();

    /// <summary>由 InstancePage 注入，用于发送指令</summary>
    public Func<string, Task>? SendCommandHandler { get; set; }

    public InstanceConsoleTab()
    {
        InitializeComponent();
        LogList.ItemsSource = _logs;
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
            LogLevel.Info    => Brushes.Green,
            LogLevel.Warn    => Brushes.Orange,
            LogLevel.Error   => Brushes.Red,
            _                => Brushes.Blue
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
