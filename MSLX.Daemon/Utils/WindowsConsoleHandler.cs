using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSLX.Daemon.Utils;

/// <summary>
/// Windows 控制台关闭事件处理器
/// 用于在控制台窗口关闭时执行清理操作
/// </summary>
public static class WindowsConsoleHandler
{
    private static readonly ILogger Logger;
    private static readonly List<Action> CleanupActions = new();
    private static readonly object LockObj = new();
    private static bool _initialized = false;

    // 保持委托引用，防止被 GC 回收
    private static HandlerRoutine? _handlerRoutine;

    // Windows API 常量
    private const int CTRL_C_EVENT = 0;
    private const int CTRL_BREAK_EVENT = 1;
    private const int CTRL_CLOSE_EVENT = 2;
    private const int CTRL_LOGOFF_EVENT = 5;
    private const int CTRL_SHUTDOWN_EVENT = 6;

    // 委托类型
    private delegate bool HandlerRoutine(int ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    static WindowsConsoleHandler()
    {
        // 创建一个简单的日志记录器
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        Logger = loggerFactory.CreateLogger("WindowsConsoleHandler");
    }

    /// <summary>
    /// 初始化控制台关闭事件处理
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (LockObj)
        {
            if (_initialized) return;

            // 创建委托实例并保持引用，防止被 GC 回收
            _handlerRoutine = OnConsoleCtrlEvent;
            SetConsoleCtrlHandler(_handlerRoutine, true);
            _initialized = true;

            Logger.LogDebug("Windows 控制台关闭事件处理器已初始化");
        }
    }

    /// <summary>
    /// 注册清理操作
    /// </summary>
    public static void RegisterCleanupAction(Action action)
    {
        lock (LockObj)
        {
            CleanupActions.Add(action);
        }
    }

    /// <summary>
    /// 取消注册清理操作
    /// </summary>
    public static void UnregisterCleanupAction(Action action)
    {
        lock (LockObj)
        {
            CleanupActions.Remove(action);
        }
    }

    private static bool OnConsoleCtrlEvent(int ctrlType)
    {
        string eventName = ctrlType switch
        {
            CTRL_C_EVENT => "CTRL_C",
            CTRL_BREAK_EVENT => "CTRL_BREAK",
            CTRL_CLOSE_EVENT => "CTRL_CLOSE",
            CTRL_LOGOFF_EVENT => "CTRL_LOGOFF",
            CTRL_SHUTDOWN_EVENT => "CTRL_SHUTDOWN",
            _ => $"UNKNOWN({ctrlType})"
        };

        Logger.LogInformation($"收到控制台关闭事件: {eventName}，正在执行清理操作...");

        // 执行所有注册的清理操作
        List<Action> actions;
        lock (LockObj)
        {
            actions = new List<Action>(CleanupActions);
        }

        foreach (var action in actions)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "执行清理操作时发生错误");
            }
        }

        // 对于 CTRL_CLOSE 事件，系统只给大约 5-10 秒的时间
        // 我们需要尽快退出
        if (ctrlType == CTRL_CLOSE_EVENT)
        {
            // 强制终止当前进程
            var currentProcess = Process.GetCurrentProcess();
            TerminateProcess(currentProcess.Handle, 0);
        }

        return true;
    }
}
