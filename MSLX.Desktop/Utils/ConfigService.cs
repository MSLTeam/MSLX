using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Threading;
using MSLX.Desktop.Models;

namespace MSLX.Desktop.Utils
{
    internal class ConfigService
    {
        public static IConfigService Config { get; } = new IConfigService();

        public static string GetAppDataPath()
        {
            if (PlatformHelper.GetOS() == PlatformHelper.TheOSPlatform.OSX)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSLX", "MSLXData");
            }
            else
            {
                return Path.Combine(AppContext.BaseDirectory, "MSLXData");
            }
        }

        public static string GetDaemonDataPath()
        {
            return Path.Combine(GetAppDataPath(), "DaemonData");
        }

        public static void InitConfig()
        {
            ConfigStore.DaemonApiKey = Config.ReadDaemonConfigKey("apiKey")?.ToString() ?? Config.ReadConfigKey("ApiKey")?.ToString() ?? string.Empty;
        }

        public static void GetDaemonApiKey()
        {
            ConfigStore.DaemonApiKey = Config.ReadDaemonConfigKey("apiKey")?.ToString() ?? Config.ReadConfigKey("ApiKey")?.ToString() ?? string.Empty;
        }

        public class IConfigService : IDisposable
        {
            private readonly string _configPath = Path.Combine(GetAppDataPath(), "Configs", "config.json");
            private readonly string _daemonConfigPath = Path.Combine(GetDaemonDataPath(), "Configs", "Config.json");

            // 缓存对象
            private JObject _configCache;
            // private JObject _daemonConfigCache;

            // 读写锁
            private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
            // private readonly ReaderWriterLockSlim _daemonConfigLock = new ReaderWriterLockSlim();

            public IConfigService()
            {
                InitializeFile(_configPath, "{}");
                // InitializeFile(_daemonConfigPath, "{}");

                // 初始化缓存
                _configCache = LoadJson<JObject>(_configPath);
                // _daemonConfigCache = LoadJson<JObject>(_daemonConfigPath);
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

            #region Config Read
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

            #endregion

            #region Daemon Config Read
            public JObject ReadDaemonConfig()
            {
                var dir = Path.GetDirectoryName(_daemonConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

                if (File.Exists(_daemonConfigPath))
                {
                        return JObject.Parse(File.ReadAllText(_daemonConfigPath));
                }
                return JObject.Parse("{}");
            }

            public JToken? ReadDaemonConfigKey(string key)
            {
                var jsonData= ReadDaemonConfig();
                try
                {
                    return jsonData.TryGetValue(key, out var value) ? value : null;
                }
                catch
                {
                    return null;
                }
            }
            #endregion

            #region Config Write
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
            #endregion

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
                // _daemonConfigLock?.Dispose();
            }
        }
    }
}
