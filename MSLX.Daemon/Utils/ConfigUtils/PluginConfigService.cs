using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils;

/// <summary>
/// 为每个插件提供独立的带缓存和读写锁的配置服务
/// </summary>
public class PluginConfigService : IDisposable
{
    private readonly string _pluginId;
    private readonly string _dataDir;
    private readonly string _configPath;
    private JObject _configCache;
    private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
    private readonly ILogger _logger;

    public PluginConfigService(string pluginId)
    {
        _pluginId = pluginId;
        _logger = ApplicationLogging.CreateLogger($"PluginConfig:{pluginId}");
        
        // 隔离目录
        _dataDir = Path.Combine(IConfigBase.GetAppDataPath(), "PluginsData", pluginId);
        _configPath = Path.Combine(_dataDir, "Config.json");

        if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);

        _configCache = InitializeConfig(_configPath);
    }

    public string GetDataPath() => _dataDir;

    private JObject InitializeConfig(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new JObject(); // 插件的默认配置是一个空对象
            File.WriteAllText(path, defaultConfig.ToString(Formatting.Indented));
            return defaultConfig;
        }

        try
        {
            var content = File.ReadAllText(path);
            return JObject.Parse(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[{_pluginId}] 配置文件读取失败，返回空配置");
            return new JObject();
        }
    }

    public JObject ReadConfig()
    {
        _configLock.EnterReadLock();
        try { return (JObject)_configCache.DeepClone(); }
        finally { _configLock.ExitReadLock(); }
    }

    public JToken? ReadConfigKey(string key)
    {
        _configLock.EnterReadLock();
        try { return _configCache.TryGetValue(key, out var value) ? value : null; }
        finally { _configLock.ExitReadLock(); }
    }

    public void WriteConfig(JObject content)
    {
        _configLock.EnterWriteLock();
        try
        {
            _configCache = (JObject)content.DeepClone();
            IConfigBase.SaveJson(_configPath, _configCache);
        }
        finally { _configLock.ExitWriteLock(); }
    }

    public void WriteConfigKey(string key, JToken value)
    {
        _configLock.EnterWriteLock();
        try
        {
            _configCache[key] = value;
            IConfigBase.SaveJson(_configPath, _configCache);
        }
        finally { _configLock.ExitWriteLock(); }
    }

    public void Dispose() => _configLock?.Dispose();
}