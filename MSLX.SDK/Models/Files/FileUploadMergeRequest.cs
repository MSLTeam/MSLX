namespace MSLX.SDK.Models.Files;

public class FileUploadMergeRequest
{ 
    public int TotalChunks { get; set; }

    /// <summary>
    /// 客户端分片大小。与 TotalSize 一起用于校验每个分片的预期长度。
    /// </summary>
    public int? ChunkSize { get; set; }

    /// <summary>
    /// 客户端文件总大小。用于合并完成后的完整性校验；为空时兼容旧客户端。
    /// </summary>
    public long? TotalSize { get; set; }

    /// <summary>
    /// 客户端完整文件校验值。用于确认合并结果与源文件一致。
    /// </summary>
    public string? Blake3 { get; set; }
}
