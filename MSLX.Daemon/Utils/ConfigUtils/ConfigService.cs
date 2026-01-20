using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class IConfigService : IDisposable
    {
        private readonly string _configPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Config.json");
        private JObject _configCache;
        private readonly ReaderWriterLockSlim _configLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public IConfigService()
        {
            _logger = ApplicationLogging.CreateLogger<IConfigService>();
            _configCache = InitializeConfig(_configPath);
        }

        /// <summary>
        /// 初始化并加载配置文件
        /// </summary>
        private JObject InitializeConfig(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                // 若文件不存在，创建默认配置
                return CreateDefaultConfig(path);
            }

            // 尝试加载现有配置
            return LoadConfigFile(path);
        }

        /// <summary>
        /// 创建默认配置文件
        /// </summary>
        private JObject CreateDefaultConfig(string path)
        {
            string apikey = StringServices.GenerateRandomString(64);
            _logger.LogInformation("正在初始化配置文件...");
            _logger.LogInformation("您的默认 API Key 是: {ApiKey}", apikey);

            var defaultConfig = new JObject
            {
                ["apiKey"] = apikey,
                ["avatar"] = "https://www.mslmc.cn/logo.png"
            };

            try
            {
                File.WriteAllText(path, defaultConfig.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "无法写入配置文件到: {Path}", path);
            }
            return defaultConfig;
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        private JObject LoadConfigFile(string path)
        {
            try
            {
                var content = File.ReadAllText(path);
                var config = JObject.Parse(content);
                bool needWrite = false;
                if (config["apiKey"] == null)
                {
                    string apikey = StringServices.GenerateRandomString(64);
                    config["apiKey"] = apikey;
                    _logger.LogInformation("您的默认 API Key 是: {ApiKey}", apikey);
                    needWrite = true;
                }
                if (config["avatar"] == null)
                {
                    config["avatar"] = "https://www.mslmc.cn/logo.png";
                    needWrite = true;
                }
                if (needWrite)
                    WriteConfig(config);
                _logger.LogInformation("成功加载配置文件");
                return config;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "配置文件格式错误，正在重新创建...");
                return RecreateConfigFile(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取配置文件失败");
                throw;
            }
        }

        /// <summary>
        /// 重新创建损坏的配置文件
        /// </summary>
        private JObject RecreateConfigFile(string path)
        {
            // 备份损坏配置
            if (File.Exists(path))
            {
                var backupPath = $"{path}.backup_{DateTime.Now:yyyyMMddHHmmss}";
                File.Move(path, backupPath);
                _logger.LogInformation("已备份损坏的配置文件到: {BackupPath}", backupPath);
            }

            // 创建新的默认配置
            return CreateDefaultConfig(path);
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
                IConfigBase.SaveJson(_configPath, _configCache);
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
                IConfigBase.SaveJson(_configPath, _configCache);
            }
            finally
            {
                _configLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _configLock?.Dispose();
        }
    }
}
