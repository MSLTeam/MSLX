namespace MSLX.SDK.Models.Files;

/// <summary>
/// 用于短期匹配同一文件的上传描述。
/// </summary>
public class FileUploadInitRequest
{
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public long? LastModified { get; set; }
    public int? ChunkSize { get; set; }
    public int? TotalChunks { get; set; }
    public string? Fingerprint { get; set; }
    public string? NodeId { get; set; }
}
