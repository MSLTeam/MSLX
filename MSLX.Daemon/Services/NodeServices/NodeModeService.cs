using System.Net;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services.NodeServices;

public static class NodeModeService
{
    // 配对持锁到关系保存完成，切换或改密钥遇到进行中的配对时直接报冲突。
    internal static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static readonly object StateLock = new();
    private static CancellationTokenSource _modeLifetime = new();
    public static bool ModeLocked { get; private set; }
    public static bool LinkKeyLocked { get; private set; }
    public static bool IsSlaveMode => (bool?)IConfigBase.Config.ReadConfigKey("IsSlaveMode") == true;

    public static bool ConfigureStartup(bool? mode, string? linkKey)
    {
        ModeLocked = mode.HasValue;
        LinkKeyLocked = !string.IsNullOrWhiteSpace(linkKey);
        bool blocked = false;
        if (mode == true && !IsSlaveMode)
        {
            using var nodes = new NodeListConfig();
            blocked = nodes.GetAllNodes().Any(n => !n.PendingRemoval);
            if (blocked) mode = false;
        }
        IConfigBase.Config.UpdateConfig(config =>
        {
            if (mode.HasValue) config["IsSlaveMode"] = mode.Value;
            if (LinkKeyLocked) config["SlaveLinkKey"] = linkKey;
            EnsureLinkKey(config);
            return config;
        });
        return blocked;
    }

    public static void UpdateSettings(bool? mode, string? linkKey, Action<JObject> update)
    {
        if (!OperationLock.Wait(0)) throw new SyncConflictException("正在处理节点配对或设置，请稍后保存");
        CancellationTokenSource? previousLifetime = null;
        try
        {
            lock (StateLock)
            {
                var wasSlave = IsSlaveMode;
                if (mode.HasValue && ModeLocked && mode != wasSlave)
                    throw new SyncConflictException("运行模式由启动参数控制，请移除 --slave 参数后重启");
                var currentKey = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "";
                if (linkKey != null && LinkKeyLocked && linkKey != currentKey)
                    throw new SyncConflictException("配对密钥由启动参数控制，请移除 --linkkey 参数后重启");
                if (linkKey != null && string.IsNullOrWhiteSpace(linkKey))
                    throw new InvalidDataException("配对密钥不能为空");
                if (mode == true && !wasSlave && IConfigBase.NodeList.GetAllNodes().Any(n => !n.PendingRemoval))
                    throw new SyncConflictException("已有已配对的子节点，请先解除配对再切换到子节点模式");

                IConfigBase.Config.UpdateConfig(config =>
                {
                    update(config);
                    if (mode.HasValue) config["IsSlaveMode"] = mode.Value;
                    if (linkKey != null) config["SlaveLinkKey"] = linkKey;
                    EnsureLinkKey(config);
                    return config;
                });
                if (wasSlave != IsSlaveMode)
                {
                    previousLifetime = _modeLifetime;
                    _modeLifetime = new CancellationTokenSource();
                }
            }
            // 发布新模式后结束旧委派连接，本机连接不订阅此取消信号。
            previousLifetime?.Cancel();
        }
        finally
        {
            previousLifetime?.Dispose();
            OperationLock.Release();
        }
    }

    public static CancellationToken CaptureAuthorization()
    {
        lock (StateLock)
        {
            RequireSlaveMode();
            return _modeLifetime.Token;
        }
    }

    public static T PublishAuthorization<T>(CancellationToken lifetime, Func<T> publish)
    {
        lock (StateLock)
        {
            RequireSlaveMode();
            if (lifetime.IsCancellationRequested)
                throw new HttpRequestException("节点运行模式已变化，请重新连接", null, HttpStatusCode.Unauthorized);
            return publish();
        }
    }

    private static void RequireSlaveMode()
    {
        if (!IsSlaveMode)
            throw new HttpRequestException("当前为主节点模式，已暂停原主节点的远程访问", null, HttpStatusCode.Unauthorized);
    }

    private static void EnsureLinkKey(JObject config)
    {
        if ((bool?)config["IsSlaveMode"] == true && string.IsNullOrWhiteSpace(config["SlaveLinkKey"]?.ToString()))
            config["SlaveLinkKey"] = StringServices.GenerateRandomString(32);
    }
}
