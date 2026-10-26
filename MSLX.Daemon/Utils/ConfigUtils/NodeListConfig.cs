using Newtonsoft.Json.Linq;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json;

namespace MSLX.Daemon.Utils.ConfigUtils
{
    public class NodeListConfig : IDisposable
    {
        private readonly string _path = Path.Combine(IConfigBase.GetAppConfigPath(), "Nodes.json");
        private JArray _nodesCache;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private static readonly JsonSerializer _pascalSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
        });

        public NodeListConfig()
        {
            InitializeFile(_path, "[]");
            _nodesCache = IConfigBase.LoadJson<JArray>(_path);
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

        public List<SlaveNodeConfig> GetAllNodes()
        {
            _lock.EnterReadLock();
            try
            {
                return [.. _nodesCache.Select(s => s.ToObject<SlaveNodeConfig>()!)];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public SlaveNodeConfig? GetNodeById(string nodeId)
        {
            _lock.EnterReadLock();
            try
            {
                return _nodesCache
                    .FirstOrDefault(n => n["NodeId"]?.ToString() == nodeId)
                    ?.ToObject<SlaveNodeConfig>();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void AddOrUpdateNode(SlaveNodeConfig node)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_nodesCache.DeepClone();
                var target = updated.FirstOrDefault(n => n["NodeId"]?.ToString() == node.NodeId);
                if (target != null)
                {
                    target.Replace(JObject.FromObject(node, _pascalSerializer));
                }
                else
                {
                    updated.Add(JObject.FromObject(node, _pascalSerializer));
                }
                IConfigBase.SaveNodeJson(_path, updated);
                _nodesCache = updated;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool MarkPendingRemoval(string nodeId) => MutateNode(nodeId, _ => true, node => node["PendingRemoval"] = true);

        public bool RemovePendingNode(string nodeId, string commsKey) => MutateNode(nodeId,
            node => node["PendingRemoval"]?.Value<bool>() == true && node["CommsKey"]?.ToString() == commsKey,
            node => node.Remove());

        public bool UpdateActiveNode(SlaveNodeConfig node) => MutateNode(node.NodeId,
            current => current["PendingRemoval"]?.Value<bool>() != true && current["CommsKey"]?.ToString() == node.CommsKey,
            current => current.Replace(JObject.FromObject(node, _pascalSerializer)));

        private bool MutateNode(string nodeId, Func<JToken, bool> matches, Action<JToken> change)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_nodesCache.DeepClone();
                var target = updated.FirstOrDefault(n => n["NodeId"]?.ToString() == nodeId);
                if (target == null || !matches(target)) return false;
                change(target);
                IConfigBase.SaveNodeJson(_path, updated);
                _nodesCache = updated;
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemoveNode(string nodeId)
        {
            _lock.EnterWriteLock();
            try
            {
                var updated = (JArray)_nodesCache.DeepClone();
                var target = updated.FirstOrDefault(n => n["NodeId"]?.ToString() == nodeId);
                if (target == null) return false;

                updated.Remove(target);
                IConfigBase.SaveNodeJson(_path, updated);
                _nodesCache = updated;
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
