using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class FrpListConfig : IDisposable
    {
        private readonly string _frpListPath = Path.Combine(IConfigBase.GetAppConfigPath(), "FrpList.json");
        private JArray _frpListCache;
        private readonly ReaderWriterLockSlim _frpListLock = new ReaderWriterLockSlim();
        private readonly ILogger _logger;

        public FrpListConfig()
        {
            _logger = ApplicationLogging.CreateLogger<FrpListConfig>();
            InitializeFile(_frpListPath, "[]");
            _frpListCache = IConfigBase.LoadJson<JArray>(_frpListPath);
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

                string folderPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Frpc", id.ToString());
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
                IConfigBase.SaveJson(_frpListPath, _frpListCache);
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
                IConfigBase.SaveJson(_frpListPath, _frpListCache);
                Directory.Delete(Path.Combine(IConfigBase.GetAppConfigPath(), "Frpc", id.ToString()), true);
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
                IConfigBase.SaveJson(_frpListPath, _frpListCache);
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

        public void Dispose()
        {
            _frpListLock?.Dispose();
        }
    }
}
