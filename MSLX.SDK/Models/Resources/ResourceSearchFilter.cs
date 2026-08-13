using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Resources
{
    public class ResourceSearchFilter
    {
        /// <summary>
        /// 如果为空或null，默认返回热度排行的列表
        /// </summary>
        [StringLength(200, ErrorMessage = "搜索关键词长度不能超过 200 个字符")]
        public string Query { get; set; }
        
        public ResourceType? Type { get; set; }
        
        // 可选参数
        [StringLength(50, ErrorMessage = "游戏版本号长度不能超过 50 个字符")]
        public string GameVersion { get; set; }

        public List<string> GameLoaders { get; set; }   // e.g. Forge, Fabric, NeoForge 等
        public List<string> PluginLoaders { get; set; } // e.g. Bukkit, Paper 等

        [StringLength(50, ErrorMessage = "分类名称长度不能超过 50 个字符")]
        public string Category { get; set; }

        public ResourceProviderType? Provider { get; set; }
        
        [Range(0, int.MaxValue, ErrorMessage = "Offset 必须大于等于 0")]
        public int Offset { get; set; }

        [Range(1, 100, ErrorMessage = "Limit 必须在 1 到 100 之间")]
        public int Limit { get; set; } = 20;
    }
}
