using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class ServerListConfig : IDisposable
    {
        private readonly string _serverListPath = Path.Combine(IConfigBase.GetAppConfigPath(), "ServerList.json");
        private JArray _serverListCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _serverListLock = new ReaderWriterLockSlim();
        // private readonly ILogger _logger;

        public ServerListConfig()
        {
            // _logger = ApplicationLogging.CreateLogger<ServerListConfig>();
            InitializeFile(_serverListPath, "[]");
            _serverListCache = IConfigBase.LoadJson<JArray>(_serverListPath);
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
                IConfigBase.SaveJson(_serverListPath, _serverListCache);
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
                if (_serverListCache.Any(s => s["ID"]?.Value<uint>() == server.ID))
                {
                    return false;
                }

                var serverObject = JObject.FromObject(server);
                _serverListCache.Add(serverObject);

                IConfigBase.SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public bool DeleteServer(uint serverId, bool deleteFiles = false)
        {
            _serverListLock.EnterWriteLock();
            try
            {
                var target = _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == serverId);

                if (target == null) return false;

                _serverListCache.Remove(target);
                IConfigBase.SaveJson(_serverListPath, _serverListCache);
                if (deleteFiles)
                {
                    var basePath = target["Base"]?.Value<string>();
                    if (!string.IsNullOrEmpty(basePath) && Directory.Exists(basePath))
                    {
                        try
                        {
                            Directory.Delete(basePath, true);
                        }
                        catch { }
                    }
                }
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
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == updatedServer.ID);

                if (target == null) return false;

                target.Replace(JObject.FromObject(updatedServer));
                IConfigBase.SaveJson(_serverListPath, _serverListCache);
                return true;
            }
            finally
            {
                _serverListLock.ExitWriteLock();
            }
        }

        public McServerInfo.ServerInfo? GetServer(uint serverId)
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache
                    .FirstOrDefault(s => s["ID"]?.Value<uint>() == serverId)
                    ?.ToObject<McServerInfo.ServerInfo>();
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public uint GenerateServerId()
        {
            _serverListLock.EnterReadLock();
            try
            {
                return _serverListCache.Any()
                    ? _serverListCache.Max(s => s["ID"]!.Value<uint>()) + 1
                    : 1;
            }
            finally
            {
                _serverListLock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            _configLock?.Dispose();
            _serverListLock?.Dispose();
        }
    }
}
