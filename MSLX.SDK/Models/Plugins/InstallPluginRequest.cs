using System.ComponentModel.DataAnnotations;


namespace MSLX.SDK.Models.Plugins;

public class InstallPluginRequest
{
    [Required(ErrorMessage = "下载地址不能为空")]
    [Url(ErrorMessage = "下载地址必须是一个有效的 HTTP/HTTPS URL")]
    [MaxLength(2048, ErrorMessage = "URL 长度超限")]
    public string DownloadUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "文件名不能为空")]
    [MaxLength(255, ErrorMessage = "文件名不能超过 255 个字符")]
    [RegularExpression(@"^[a-zA-Z0-9_\-\.]+\.(dll|new)$", ErrorMessage = "文件名非法：只能包含字母、数字、横杠、下划线、点，且必须以 .dll 或 .new 结尾（严禁包含路径符号）")]
    public string FileName { get; set; } = string.Empty;

    public bool Overwrite { get; set; } = false;
}

public class TaskStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string Message { get; set; } = string.Empty;
}

