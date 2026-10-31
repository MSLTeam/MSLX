namespace MSLX.SDK.Models.Files;

public class FileUploadMergeRequest
{ 
    public int TotalChunks { get; set; }

    /// <summary>
    /// 客户端文件总大小。用于合并完成后的完整性校验；为空时兼容旧客户端。
    /// </summary>
    public long? TotalSize { get; set; }
}
