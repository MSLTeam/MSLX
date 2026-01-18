using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Threading;
using static MSLX.Desktop.Models.MCServerModel;
using System.Linq;

namespace MSLX.Desktop.Utils
{
    internal class ConfigService
    {
        public static IConfigService Config { get; } = new IConfigService();
        public static ServerListConfig ServerList { get; } = new ServerListConfig();
        public static FrpListConfig FrpList { get; } = new FrpListConfig();

        public static string GetAppDataPath()
        {
            if (PlatformHelper.GetOS() == PlatformHelper.TheOSPlatform.OSX)
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
            private readonly string _configPath = Path.Combine(ConfigService.GetAppDataPath(), "Configs", "config.json");

            // 缓存对象
            private JObject _configCache;

            // 读写锁
            private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();

            public IConfigService()
            {
                InitializeFile(_configPath, "{}");

                // 初始化缓存
                _configCache = LoadJson<JObject>(_configPath);
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
            private readonly string _serverListPath = Path.Combine(ConfigService.GetAppDataPath(), "Configs", "ServerList.json");

            // 缓存对象
            private JArray _serverListCache;

            // 读写锁
            private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
            private readonly ReaderWriterLockSlim _serverListLock = new ReaderWriterLockSlim();

            public ServerListConfig()
            {
                InitializeFile(_serverListPath, "[]");

                // 初始化缓存
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

            public List<ServerInfo> GetServerList()
            {
                _serverListLock.EnterReadLock();
                try
                {
                    return [.. _serverListCache.Select(s => s.ToObject<ServerInfo>()!)];
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

            public bool CreateServer(ServerInfo server)
            {
                _serverListLock.EnterWriteLock();
                try
                {
                    // 检查ID是否已存在
                    if (_serverListCache.Any(s => s["ID"]?.Value<int>() == server.ID))
                    {
                        return false;
                    }

                    // 转换为JObject并添加
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
                    // 查找要删除的服务器
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

            // 可选的更新方法
            public bool UpdateServer(ServerInfo updatedServer)
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

            // 获取单个服务器的方法
            public ServerInfo? GetServer(int serverId)
            {
                _serverListLock.EnterReadLock();
                try
                {
                    return _serverListCache
                        .FirstOrDefault(s => s["ID"]?.Value<int>() == serverId)
                        ?.ToObject<ServerInfo>();
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
            private readonly string _frpListPath = Path.Combine(ConfigService.GetAppDataPath(), "Configs", "FrpList.json");
            private JArray _frpListCache;
            private readonly ReaderWriterLockSlim _frpListLock = new ReaderWriterLockSlim();

            public FrpListConfig()
            {
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
                int id = NumberHelper.GetRandomNumber(10000000, 99999999);
                _frpListLock.EnterWriteLock();
                try
                {
                    if (_frpListCache.Any(s => s["ID"]?.Value<int>() == id))
                        return false;

                    // 写入frpc配置文件 （为数不多好懂的地方捏～）
                    string folderPath = Path.Combine(ConfigService.GetAppDataPath(), "Configs", "Frpc", id.ToString());
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
                    // 删除配置文件文件夹
                    Directory.Delete(Path.Combine(ConfigService.GetAppDataPath(), "Configs", "Frpc", id.ToString()), true);
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
}
