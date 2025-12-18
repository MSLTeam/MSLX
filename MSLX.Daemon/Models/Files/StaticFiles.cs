using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Files;

public class UploadStaticFilesRequest
{
    [Required(ErrorMessage = "文件Key (fileKey) 不能为空")]
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "fileKey 格式不正确")]
    public required string FileKey { get; set; } 
    
    [Required(ErrorMessage = "文件 (fileName) 不能为空")]
    public required string FileName { get; set; } 
    
}