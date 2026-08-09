using System;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Threading;
using MSLX.Desktop.Models;
using System.Diagnostics;

namespace MSLX.Desktop.Utils
{
    public static class ConfigService
    {
        public static IConfigService Config { get; } = new();

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

        public static void GetDaemonApiKey()
        {
            ConfigStore.DaemonApiKey = Config.ReadDaemonConfigKey("apiKey")?.ToString() ?? Config.ReadConfigKey("ApiKey")?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// 根据本地 Daemon 配置生成 Desktop 使用的连接地址。
        /// </summary>
        public static string GetLocalDaemonAddress()
        {
            // 内置 Daemon 固定监听 localhost，避免错误的监听地址配置导致 Desktop 无法连接。
            bool isMacAppBundle = PlatformHelper.IsMacAppBundle();
            JObject config = Config.ReadDaemonConfig();
            string host = isMacAppBundle
                ? "localhost"
                : config["listenHost"]?.ToString()?.Trim() ?? string.Empty;
            string portText = config["listenPort"]?.ToString()?.Trim() ?? string.Empty;
            bool enableSsl = bool.TryParse(config["enableSsl"]?.ToString(), out bool ssl) && ssl;

            if (string.IsNullOrWhiteSpace(host))
            {
                host = "localhost";
            }
            else if (host is "*" or "+" or "0.0.0.0" or "[::]" or "::")
            {
                host = "localhost";
            }
            else if (host.Contains(':') && !host.StartsWith('[') && !host.EndsWith(']'))
            {
                host = $"[{host}]";
            }

            if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
            {
                port = 1027;
            }

            return $"{(enableSsl ? "https" : "http")}://{host}:{port}";
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

                ConfigStore.Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
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
                    try
                    {
                        return JObject.Parse(File.ReadAllText(_daemonConfigPath));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
                return JObject.Parse("{}");
            }

            public JToken? ReadDaemonConfigKey(string key)
            {
                var jsonData = ReadDaemonConfig();
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
