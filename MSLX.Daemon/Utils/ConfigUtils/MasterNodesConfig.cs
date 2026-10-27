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
                var updated = (JArray)_mastersCache.DeepClone();
                var target = updated.FirstOrDefault(m => m["MasterId"]?.ToString() == master.MasterId || (!string.IsNullOrEmpty(master.NodeId) && m["NodeId"]?.ToString() == master.NodeId));
                if (target != null)
                {
                    target.Replace(JObject.FromObject(master, _pascalSerializer));
                }
                else
                {
                    updated.Add(JObject.FromObject(master, _pascalSerializer));
                }
                IConfigBase.SaveNodeJson(_path, updated);
                _mastersCache = updated;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public MasterNodeInfo AcceptPairing(AcceptLinkRequest request)
        {
            _lock.EnterWriteLock();
            try
            {
                var existing = _mastersCache.FirstOrDefault(m => m["MasterId"]?.ToString() == request.MasterId)?.ToObject<MasterNodeInfo>();
                if (existing != null && existing.PairingId == request.PairingId) return existing;
                var master = new MasterNodeInfo { MasterId = request.MasterId, PairingId = request.PairingId,
                    NodeId = Guid.NewGuid().ToString("N"), CommsKey = StringServices.GenerateRandomString(32),
                    MasterUrl = request.MasterUrl, LinkedAt = DateTime.Now };
                var updated = (JArray)_mastersCache.DeepClone();
                var target = updated.FirstOrDefault(m => m["MasterId"]?.ToString() == request.MasterId);
                if (target == null) updated.Add(JObject.FromObject(master, _pascalSerializer));
                else target.Replace(JObject.FromObject(master, _pascalSerializer));
                IConfigBase.SaveNodeJson(_path, updated);
                _mastersCache = updated;
                return master;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool UpdateRelationship(UpdateLinkRequest request)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_mastersCache.DeepClone();
                var target = updated.FirstOrDefault(m => m["CommsKey"]?.ToString() == request.CommsKey &&
                    (m["NodeId"]?.ToString() == request.NodeId || string.IsNullOrEmpty(m["NodeId"]?.ToString())));
                if (target == null) return false;
                target["NodeId"] = request.NodeId;
                target["MasterUrl"] = request.MasterUrl;
                IConfigBase.SaveNodeJson(_path, updated);
                _mastersCache = updated;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemoveRelationship(string nodeId, string commsKey)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_mastersCache.DeepClone();
                var target = updated.FirstOrDefault(m => m["CommsKey"]?.ToString() == commsKey &&
                    (m["NodeId"]?.ToString() == nodeId || string.IsNullOrEmpty(m["NodeId"]?.ToString())));
                if (target == null) return false;
                target.Remove();
                IConfigBase.SaveNodeJson(_path, updated);
                _mastersCache = updated;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemoveMaster(string masterId)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_mastersCache.DeepClone();
                var target = updated.FirstOrDefault(m => m["MasterId"]?.ToString() == masterId || m["NodeId"]?.ToString() == masterId);
                if (target == null) return false;

                updated.Remove(target);
                IConfigBase.SaveNodeJson(_path, updated);
                _mastersCache = updated;
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
