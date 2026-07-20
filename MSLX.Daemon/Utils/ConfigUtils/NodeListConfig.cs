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
                var target = _nodesCache.FirstOrDefault(n => n["NodeId"]?.ToString() == node.NodeId);
                if (target != null)
                {
                    target.Replace(JObject.FromObject(node, _pascalSerializer));
                }
                else
                {
                    _nodesCache.Add(JObject.FromObject(node, _pascalSerializer));
                }
                IConfigBase.SaveJson(_path, _nodesCache);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool RemoveNode(string nodeId)
        {
            _lock.EnterWriteLock();
            try
            {
                var target = _nodesCache.FirstOrDefault(n => n["NodeId"]?.ToString() == nodeId);
                if (target == null) return false;

                _nodesCache.Remove(target);
                IConfigBase.SaveJson(_path, _nodesCache);
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
