﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static MSLX.Models.MCServerModel;

namespace MSLX.Core.Utils
{
    public class ConfigService : IDisposable
    {
        private readonly string _configPath = Path.Combine(AppContext.BaseDirectory, "Configs", "config.json");
        private readonly string _serverListPath = Path.Combine(AppContext.BaseDirectory, "Configs", "ServerList.json");

        // 缓存对象
        private JObject _configCache;
        private JArray _serverListCache;

        // 读写锁
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _serverListLock = new ReaderWriterLockSlim();

        public ConfigService()
        {
            InitializeFile(_configPath, "{}");
            InitializeFile(_serverListPath, "[]");

            // 初始化缓存
            _configCache = LoadJson<JObject>(_configPath);
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
}
