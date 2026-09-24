namespace MSLX.SDK.Models.Files;

/// <summary>
/// 归档文件条目解压安全校验失败原因
/// </summary>
public enum ExtractPathFailureReason
{
    /// <summary>
    /// 无错误，通过校验
    /// </summary>
    None = 0,

    /// <summary>
    /// 条目路径为空或空白
    /// </summary>
    EmptyOrWhitespace = 1,

    /// <summary>
    /// 检测到 Zip Slip 路径穿越（试图逃逸到根目录之外）
    /// </summary>
    PathTraversal = 2,

    /// <summary>
    /// 条目路径被上层安全策略/实例沙箱拒绝
    /// </summary>
    ForbiddenByPolicy = 3
}
