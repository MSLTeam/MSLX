namespace MSLX.SDK.Attributes;

/// <summary>
/// 声明当前控制器或路由端点所允许的降权 Token 权限范围（Scope）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class AllowTokenScopeAttribute : Attribute
{
    /// <summary>
    /// 允许访问当前端点的 Scope 集合（如 "media", "download"）
    /// </summary>
    public string[] AllowedScopes { get; }

    public AllowTokenScopeAttribute(params string[] allowedScopes)
    {
        AllowedScopes = allowedScopes ?? Array.Empty<string>();
    }
}

/// <summary>
/// MSLX 内置的标准降权凭据权限范围常量
/// </summary>
public static class TokenScopes
{
    /// <summary>
    /// 文件下载专用凭据（默认有效期 2 小时，支持断点续传）
    /// </summary>
    public const string Download = "download";

    /// <summary>
    /// 媒体资源专用凭据（与用户登录会话寿命对齐，适用于图标、地图切片、缩略图等）
    /// </summary>
    public const string Media = "media";
}
