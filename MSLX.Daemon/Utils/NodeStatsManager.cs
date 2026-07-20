using System.Collections.Concurrent;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Utils
{

    public static class NodeStatsManager
    {
        public class NodeStatsEntry
        {
            public NodeStatsPayload Stats { get; set; } = new();
            public DateTime LastUpdated { get; set; }
        }

        public static ConcurrentDictionary<string, NodeStatsEntry> SlaveStats { get; } = new();

        public static void UpdateNodeStats(string nodeId, NodeStatsPayload stats)
        {
            SlaveStats[nodeId] = new NodeStatsEntry { Stats = stats, LastUpdated = DateTime.Now };
        }

        public static void RemoveNodeStats(string nodeId)
        {
            SlaveStats.TryRemove(nodeId, out _);
        }
    }
}
