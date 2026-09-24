using System.ComponentModel.DataAnnotations;
using MSLX.SDK.Attributes;

namespace MSLX.SDK.Models.Files;

public class OfflineDownloadRequest
{
    [Required(ErrorMessage = "下载地址不能为空")]
    [Url(ErrorMessage = "请输入有效的URL地址")]
    [SsrfSafeUrl]
    public string Url { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true, ErrorMessage = "路径参数缺失")]
    public string Path { get; set; } = string.Empty;

    public string? FileName { get; set; }
}