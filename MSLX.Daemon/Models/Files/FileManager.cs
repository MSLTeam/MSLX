namespace MSLX.Daemon.Models.Files;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "file"; // file / folder
    public long Size { get; set; } // 字节大小
    public DateTime LastModified { get; set; }
    public string Permission { get; set; } = ""; 
}

public class SaveFileRequest
{
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class RenameFileRequest
{
    public string OldPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
}

public class DeleteFileRequest
{
    public List<string> Paths { get; set; } = new();
}

public class SaveUploadRequest
{
    public string UploadId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
}

public class CompressRequest
{
    public List<string> Sources { get; set; } = new(); 
    public string TargetName { get; set; } = string.Empty; 
    public string CurrentPath { get; set; } = string.Empty;
}

public class TaskStatusResponse
{
    public string Status { get; set; } = "pending"; // pending, processing, success, error
    public int Progress { get; set; } // 0 - 100
    public string Message { get; set; } = "";
}

public class DecompressRequest
{
    public string FileName { get; set; } 
    public string CurrentPath { get; set; } 
    public string Encoding { get; set; } = "auto";
    public bool CreateSubFolder { get; set; } = false;
}

public class ChmodRequest
{
    public string Path { get; set; } = string.Empty;
    public string Mode { get; set; } = "755";
}