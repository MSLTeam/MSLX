using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Node
{
    public class NodeStatsPayload
    {
        [Required(ErrorMessage = "CPU使用率不能为空")]
        public double cpu { get; set; }

        [Required(ErrorMessage = "内存总量不能为空")]
        public double memTotal { get; set; }

        [Required(ErrorMessage = "已用内存不能为空")]
        public double memUsed { get; set; }

        [Required(ErrorMessage = "内存使用率不能为空")]
        public double memUsage { get; set; }

        [Required(ErrorMessage = "时间戳不能为空")]
        public string timestamp { get; set; } = string.Empty;
    }
}
