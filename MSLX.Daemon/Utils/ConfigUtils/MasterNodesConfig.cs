using Newtonsoft.Json.Linq;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class MasterNodesConfig : IDisposable
    {
        private readonly string _path = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        private JArray _mastersCache;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private static readonly JsonSerializer _pascalSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
        });

        public MasterNodesConfig()
        {
            InitializeFile(_path, "[]");
            _mastersCache = IConfigBase.LoadJson<JArray>(_path);
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

        public List<MasterNodeInfo> GetAllMasters()
        {
            _lock.EnterReadLock();
            try
            {
                return [.. _mastersCache.Select(s => s.ToObject<MasterNodeInfo>()!)];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public MasterNodeInfo? GetMasterById(string masterId)
        {
            _lock.EnterReadLock();
            try
            {
                var target = _mastersCache
                    .FirstOrDefault(m => m["MasterId"]?.ToString() == masterId || m["NodeId"]?.ToString() == masterId)
                    ?.ToObject<MasterNodeInfo>();

                if (target == null && _mastersCache.Count == 1)
                {
                    return _mastersCache[0].ToObject<MasterNodeInfo>();
                }

                return target;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void AddOrUpdateMaster(MasterNodeInfo master)
        {
            _lock.EnterWriteLock();
            try
            {
                var target = _mastersCache.FirstOrDefault(m => m["MasterId"]?.ToString() == master.MasterId || (!string.IsNullOrEmpty(master.NodeId) && m["NodeId"]?.ToString() == master.NodeId));
                if (target != null)
                {
                    target.Replace(JObject.FromObject(master, _pascalSerializer));
                }
                else
                {
                    _mastersCache.Add(JObject.FromObject(master, _pascalSerializer));
                }
                IConfigBase.SaveJson(_path, _mastersCache);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool RemoveMaster(string masterId)
        {
            _lock.EnterWriteLock();
            try
            {
                var target = _mastersCache.FirstOrDefault(m => m["MasterId"]?.ToString() == masterId || m["NodeId"]?.ToString() == masterId);
                if (target == null) return false;

                _mastersCache.Remove(target);
                IConfigBase.SaveJson(_path, _mastersCache);
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}
