using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Files;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "file"; 
    public long Size { get; set; } 
    public DateTime LastModified { get; set; }
    public string Permission { get; set; } = ""; 
}

public class SaveFileRequest
{
    [Required(ErrorMessage = "文件路径 (Path) 不能为空")]
    public string Path { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}

public class RenameFileRequest
{
    [Required(ErrorMessage = "原路径 (OldPath) 不能为空")]
    public string OldPath { get; set; } = string.Empty;

    [Required(ErrorMessage = "新路径 (NewPath) 不能为空")]
    public string NewPath { get; set; } = string.Empty;
}

public class DeleteFileRequest
{
    [Required(ErrorMessage = "删除列表 (Paths) 不能为空")]
    [MinLength(1, ErrorMessage = "至少需要指定一个要删除的文件或目录")]
    public List<string> Paths { get; set; } = new();
}

public class SaveUploadRequest
{
    [Required(ErrorMessage = "上传会话ID (UploadId) 不能为空")]
    public string UploadId { get; set; } = string.Empty;

    [Required(ErrorMessage = "文件名 (FileName) 不能为空")]
    public string FileName { get; set; } = string.Empty;

    public string CurrentPath { get; set; } = string.Empty;
}

public class CompressRequest
{
    [Required(ErrorMessage = "压缩源列表 (Sources) 不能为空")]
    [MinLength(1, ErrorMessage = "至少需要选择一个文件或目录进行压缩")]
    public List<string> Sources { get; set; } = new(); 

    [Required(ErrorMessage = "目标文件名 (TargetName) 不能为空")]
    [RegularExpression(@"^[\w\-. ]+\.(zip|tar\.gz)$", ErrorMessage = "目标文件名格式错误或不支持该后缀")]
    public string TargetName { get; set; } = string.Empty; 

    public string CurrentPath { get; set; } = string.Empty;
}

public class DecompressRequest
{
    [Required(ErrorMessage = "压缩包文件名 (FileName) 不能为空")]
    public string FileName { get; set; } = string.Empty;

    public string CurrentPath { get; set; } = string.Empty;

    [AllowedValues("auto", "utf-8", "gbk", "gb2312", ErrorMessage = "不支持的编码格式")]
    public string Encoding { get; set; } = "auto";

    public bool CreateSubFolder { get; set; } = false;
}

public class ChmodRequest
{
    [Required(ErrorMessage = "路径 (Path) 不能为空")]
    public string Path { get; set; } = string.Empty;

    [Required(ErrorMessage = "权限模式 (Mode) 不能为空")]
    [RegularExpression(@"^[0-7]{3}$", ErrorMessage = "权限模式必须为3位八进制数字 (例如 755)")]
    public string Mode { get; set; } = "755";
}

public class TaskStatusResponse
{
    public string Status { get; set; } = "pending"; 
    public int Progress { get; set; } 
    public string Message { get; set; } = "";
}

public class CreateDirectoryRequest
{
    public string Path { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "新目录名称 (name) 不能为空")]
    public string Name { get; set; } = string.Empty;
}