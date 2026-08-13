using System;
using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Resources
{
    public class Resource
    {
        [Required(ErrorMessage = "资源 ID 不能为空")]
        public string Id { get; set; }

        [Required(ErrorMessage = "资源名称不能为空")]
        public string Name { get; set; }

        public string Summary { get; set; }
        public string IconUrl { get; set; }
        public string Author { get; set; }
        public long DownloadCount { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ResourceProviderType Provider { get; set; }
    }
}
