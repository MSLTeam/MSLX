using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils;

public static class ConfigServices
{
    public static IConfigService Config { get; private set; } = null!;
    public static ServerListConfig ServerList { get; private set; } = null!;
    public static FrpListConfig FrpList { get; private set; } = null!;

    public static void Initialize(ILoggerFactory loggerFactory)
    {
        ApplicationLogging.LoggerFactory = loggerFactory;

        Config = new IConfigService();
        ServerList = new ServerListConfig();
        FrpList = new FrpListConfig();
    }

    public static string GetAppDataPath()
    {
        if (PlatFormServices.GetOs() == "MacOS")
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSLX");
        }
        else
        {
            return AppContext.BaseDirectory;
        }
    }

    public class IConfigService : IDisposable
    {
        private readonly string _configPath = Path.Combine(GetAppDataPath(), "Configs", "Config.json");
        private JObject _configCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public IConfigService()
        {
            _logger = ApplicationLogging.CreateLogger<IConfigService>();
            InitializeFile(_configPath);
            _configCache = LoadJson<JObject>(_configPath);
        }

        private void InitializeFile(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                string apikey = StringServices.GenerateRandomString(64);

                _logger.LogInformation("正在初始化配置文件...");
                _logger.LogInformation("您的默认 API Key 是: {ApiKey}", apikey);

                File.WriteAllText(path, $"{{\n    \"api-key\":\"{apikey}\",\n    \"user\":\"MSLX用户\",\n    \"avatar\":\"https://www.mslmc.cn/logo.png\"\n}}");
            }
        }

        public JObject ReadConfig()
        {
            _configLock.EnterReadLock();
            try
            {
                return (JObject)_configCache.DeepClone();
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        public JToken? ReadConfigKey(string key)
        {
            _configLock.EnterReadLock();
            try
            {
                return _configCache.TryGetValue(key, out var value) ? value : null;
            }
            finally
            {
                _configLock.ExitReadLock();
            }
        }

        public void WriteConfig(JObject content)
        {
            _configLock.EnterWriteLock();
            try
            {
                _configCache = (JObject)content.DeepClone();
                SaveJson(_configPath, _configCache);
            }
            finally
            {
                _configLock.ExitWriteLock();
            }
        }

        public void WriteConfigKey(string key, JToken value)
        {
            _configLock.EnterWriteLock();
            try
            {
                _configCache[key] = value;
                SaveJson(_configPath, _configCache);
            }
            finally
            {
                _configLock.ExitWriteLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _configLock?.Dispose();
        }
    }

    public class ServerListConfig : IDisposable
    {
        private readonly string _serverListPath = Path.Combine(GetAppDataPath(), "Configs", "ServerList.json");
        private JArray _serverListCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _serverListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public ServerListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<ServerListConfig>();
            InitializeFile(_serverListPath, "[]");
            _serverListCache = LoadJson<JArray>(_serverListPath);
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
        }

        public JArray ReadServerList()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return (JArray)_serverListCache.DeepClone();
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public List<McServerInfo.ServerInfo> GetServerList()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return [.. _serverListCache.Select(s => s.ToObject<McServerInfo.ServerInfo>()!)];
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public void WriteServerList(JArray content)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                _serverListCache = (JArray)content.DeepClone();
                SaveJson(_serverListPath, _serverListCache);
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool CreateServer(McServerInfo.ServerInfo server)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                if (_serverListCache.Any(s => s["ID"]?.Value<int>() == server.ID))
                {
                    return false;
                }

                var serverObject = JObject.FromObject(server);
                _serverListCache.Add(serverObject);

                SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool DeleteServer(int serverId)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                var target = _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<int>() == serverId);

                if (target == null) return false;

                _serverListCache.Remove(target);
                SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool UpdateServer(McServerInfo.ServerInfo updatedServer)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                var target = _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<int>() == updatedServer.ID);

                if (target == null) return false;

                target.Replace(JObject.FromObject(updatedServer));
                SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public McServerInfo.ServerInfo? GetServer(int serverId)
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<int>() == serverId)
                    ?.ToObject<McServerInfo.ServerInfo>();
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public int GenerateServerId()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache.Any()
                    ? _serverListCache.Max(s => s["ID"]!.Value<int>()) + 1
                    : 1;
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _configLock?.Dispose();
            _serverListLock?.Dispose();
        }
    }

    public class FrpListConfig : IDisposable
    {
        private readonly string _frpListPath = Path.Combine(GetAppDataPath(), "Configs", "FrpList.json");
        private JArray _frpListCache;
        private readonly ReaderWriterLockSlim _frpListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public FrpListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<FrpListConfig>();
            InitializeFile(_frpListPath, "[]");
            _frpListCache = LoadJson<JArray>(_frpListPath);
        }

        private void InitializeFile(string path, string defaultContent)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, defaultContent);
            }
        }

        public JArray ReadFrpList()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return (JArray)_frpListCache.DeepClone();
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public List<JToken> GetFrpList()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.ToList();
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public bool CreateFrpConfig(string name, string server, string configType, string config)
        {
            int id = StringServices.GetRandomNumber(10000000, 99999999);
            _frpListLock.EnterWriteLock();
            try
            {
                if (_frpListCache.Any(s => s["ID"]?.Value<int>() == id))
                    return false;

                string folderPath = Path.Combine(GetAppDataPath(), "Configs", "Frpc", id.ToString());
                string filePath = Path.Combine(folderPath, $"frpc.{configType}");
                try
                {
                    Directory.CreateDirectory(folderPath);
                    File.WriteAllText(filePath, config);
                }
                catch
                {
                    return false;
                }

                var newItem = new JObject
                {
                    ["ID"] = id,
                    ["Name"] = name,
                    ["ConfigType"] = configType,
                    ["Service"] = server
                };
                _frpListCache.Add(newItem);
                SaveJson(_frpListPath, _frpListCache);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public bool DeleteFrpConfig(int id)
        {
            _frpListLock.EnterWriteLock();
            try
            {
                var target = _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id);
                if (target == null) return false;

                _frpListCache.Remove(target);
                SaveJson(_frpListPath, _frpListCache);
                Directory.Delete(Path.Combine(GetAppDataPath(), "Configs", "Frpc", id.ToString()), true);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public bool UpdateFrpConfig(int id, string name, string server, string configType)
        {
            _frpListLock.EnterWriteLock();
            try
            {
                var target = _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id);
                if (target == null) return false;

                target["Name"] = name;
                target["Service"] = server;
                target["ConfigType"] = configType;
                SaveJson(_frpListPath, _frpListCache);
                return true;
            }
            finally
            {
                _frpListLock.ExitWriteLock();
            }
        }

        public JObject? GetFrpConfig(int id)
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.FirstOrDefault(s => s["ID"]?.Value<int>() == id) as JObject;
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        public int GenerateFrpId()
        {
            _frpListLock.EnterReadLock();
            try
            {
                return _frpListCache.Any()
                    ? _frpListCache.Max(s => s["ID"]!.Value<int>()) + 1
                    : 1;
            }
            finally
            {
                _frpListLock.ExitReadLock();
            }
        }

        private T LoadJson<T>(string path) where T : JToken
        {
            var content = File.ReadAllText(path);
            return JToken.Parse(content) as T ?? throw new InvalidDataException("Invalid JSON format");
        }

        private void SaveJson<T>(string path, T data) where T : JToken
        {
            File.WriteAllText(path, data.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public void Dispose()
        {
            _frpListLock?.Dispose();
        }
    }
}