using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Plugins;

public class UploadPluginRequest
{
    [Required(ErrorMessage = "文件 ID 不能为空")]
    public string FileId { get; set; } = string.Empty;

    public string? FileName { get; set; }
}
