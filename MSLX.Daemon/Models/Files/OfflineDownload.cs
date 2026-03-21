using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Files;

public class OfflineDownloadRequest
{
    [Required(ErrorMessage = "下载地址不能为空")]
    [Url(ErrorMessage = "请输入有效的URL地址")]
    public string Url { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true, ErrorMessage = "路径参数缺失")]
    public string Path { get; set; } = string.Empty;

    public string? FileName { get; set; }
}