using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Resources
{
    public class ResourceVersion
    {
        [Required(ErrorMessage = "版本 ID 不能为空")]
        public string Id { get; set; }

        [Required(ErrorMessage = "版本名称不能为空")]
        public string Name { get; set; }

        public string VersionNumber { get; set; }
        public List<string> GameVersions { get; set; }
        public List<string> Loaders { get; set; }

        [Required(ErrorMessage = "下载链接不能为空")]
        public string DownloadUrl { get; set; }

        public string Filename { get; set; }
        public long FileSizeBytes { get; set; }

        [Range(0, 1, ErrorMessage = "Environment 必须为 0 (客户端包) 或 1 (服务端包)")]
        public int Environment { get; set; } // 0 = Client, 1 = Server
    }
}
