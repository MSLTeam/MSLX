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