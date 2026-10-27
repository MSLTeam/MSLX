using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Plugins;

public class UploadPluginRequest
{
    [Required(ErrorMessage = "文件 ID 不能为空")]
    public string FileId { get; set; } = string.Empty;

    public string? FileName { get; set; }
    public bool Overwrite { get; set; } = true;
    public string? ExpectedPluginId { get; set; }
    public string? PackageHash { get; set; }
    public string? ExpectedRevision { get; set; }
    public bool? Enabled { get; set; }
    public Newtonsoft.Json.Linq.JObject? Config { get; set; }
}
