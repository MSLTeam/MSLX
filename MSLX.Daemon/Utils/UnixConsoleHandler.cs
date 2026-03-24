using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MSLX.Daemon.Utils;

/// <summary>
/// Unix/Linux/macOS 控制台关闭事件处理器
/// 用于在进程收到终止信号时执行清理操作
/// </summary>
public static class UnixConsoleHandler
{
    private static readonly ILogger Logger;
    private static readonly List<Action> CleanupActions = new();
    private static readonly object LockObj = new();
    private static bool _initialized = false;

    // 保持委托引用，防止被 GC 回收
    private static SignalHandler? _signalHandler;
    private static GCHandle _signalHandlerHandle;

    // POSIX 信号常量
    private const int SIGINT = 2;   // Ctrl+C
    private const int SIGTERM = 15; // 终止信号 (kill 默认)
    private const int SIGHUP = 1;   // 挂起信号 (终端关闭)

    // 委托类型
    private delegate void SignalHandler(int signum);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr signal(int signum, SignalHandler handler);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpid();

    static UnixConsoleHandler()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        Logger = loggerFactory.CreateLogger("UnixConsoleHandler");
    }

    /// <summary>
    /// 初始化 Unix 信号处理
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (LockObj)
        {
            if (_initialized) return;

            // 创建委托实例并保持引用
            _signalHandler = OnSignal;
            _signalHandlerHandle = GCHandle.Alloc(_signalHandler, GCHandleType.Normal);

            // 注册信号处理器
            signal(SIGINT, _signalHandler);
            signal(SIGTERM, _signalHandler);
            signal(SIGHUP, _signalHandler);

            _initialized = true;

            Logger.LogDebug("Unix 信号处理器已初始化");
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

    private static void OnSignal(int signum)
    {
        string signalName = signum switch
        {
            SIGINT => "SIGINT (Ctrl+C)",
            SIGTERM => "SIGTERM",
            SIGHUP => "SIGHUP (Terminal closed)",
            _ => $"Signal({signum})"
        };

        Logger.LogInformation($"收到信号: {signalName}，正在执行清理操作...");

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

        // 根据信号类型决定是否退出
        if (signum == SIGINT)
        {
            // Ctrl+C 正常退出
            Environment.Exit(0);
        }
        else
        {
            // 其他信号也正常退出
            Environment.Exit(0);
        }
    }
}
