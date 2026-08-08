using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Docker;

/// <summary>
/// Docker 环境探测结果
/// </summary>
public class DockerEnvStatus
{
    /// <summary>docker 可用性</summary>
    public bool Available { get; set; }

    /// <summary>Docker 客户端版本</summary>
    public string? ClientVersion { get; set; }

    /// <summary>Docker 服务端版本，为空说明连不上</summary>
    public string? ServerVersion { get; set; }

    /// <summary>服务端系统类型 linux / windows （正常都linux）</summary>
    public string? OsType { get; set; }

    /// <summary>MSLX 自身是否运行在容器内</summary>
    public bool InContainer { get; set; }

    /// <summary>容器内是否挂载了 /var/run/docker.sock</summary>
    public bool SockMounted { get; set; }

    /// <summary>不可用的原因分类：notInstalled / daemonUnreachable / sockNotMounted / permissionDenied / unknown</summary>
    public string? ErrorType { get; set; }

    /// <summary>不可用时的原始错误信息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 本地镜像信息
/// </summary>
public class DockerImageInfo
{
    public string Repository { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;

    /// <summary>完整引用</summary>
    public string Reference { get; set; } = string.Empty;

    public string ImageId { get; set; } = string.Empty;

    /// <summary>短 ID（12 位）</summary>
    public string ShortId { get; set; } = string.Empty;

    public string? Digest { get; set; }

    /// <summary>已格式化的大小（带单位）</summary>
    public string Size { get; set; } = string.Empty;

    /// <summary>解析后的字节数</summary>
    public long? SizeBytes { get; set; }

    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>是否是无 tag 的悬空镜像</summary>
    public bool IsDangling { get; set; }

    /// <summary>是否是 MSLX 内置运行时镜像</summary>
    public bool IsMslxRuntime { get; set; }

    /// <summary>引用了该镜像的实例（ID 与名称）</summary>
    public List<DockerImageUsage> UsedBy { get; set; } = new();
}

/// <summary>
/// 镜像被实例引用的记录
/// </summary>
public class DockerImageUsage
{
    public int InstanceId { get; set; }
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>实例配置里原始填写的镜像值</summary>
    public string ConfiguredImage { get; set; } = string.Empty;
}

/// <summary>
/// 内置运行时镜像
/// </summary>
public class DockerPresetImage
{
    /// <summary>伪协议，如 MSLX://DockerImage/Java/21</summary>
    public string Pseudo { get; set; } = string.Empty;

    /// <summary>解析后的真实镜像地址</summary>
    public string Image { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>本地是否已存在</summary>
    public bool Exists { get; set; }

    public string? Size { get; set; }
}

/// <summary>
/// 镜像详情
/// </summary>
public class DockerImageDetail
{
    public string ImageId { get; set; } = string.Empty;
    public List<string> RepoTags { get; set; } = new();
    public List<string> RepoDigests { get; set; } = new();
    public string? Created { get; set; }
    public string? Architecture { get; set; }
    public string? Os { get; set; }
    public long Size { get; set; }
    public string? WorkingDir { get; set; }
    public List<string> Env { get; set; } = new();
    public List<string> Entrypoint { get; set; } = new();
    public List<string> Cmd { get; set; } = new();
    public List<string> ExposedPorts { get; set; } = new();
    public List<string> Volumes { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
    public List<string> Layers { get; set; } = new();

    /// <summary>原始 inspect JSON</summary>
    public string? Raw { get; set; }
}

public class DockerPullRequest
{
    /// <summary>镜像引用，支持 MSLX:// 伪协议；不含 tag 时默认 latest</summary>
    [Required(ErrorMessage = "镜像名称不能为空")]
    public string Image { get; set; } = string.Empty;

    /// <summary>可选平台，如 linux/amd64</summary>
    [RegularExpression(@"^[a-zA-Z0-9/._-]+$", ErrorMessage = "平台参数格式不合法")]
    public string? Platform { get; set; }
}

public class DockerImageDeleteRequest
{
    [Required(ErrorMessage = "镜像引用不能为空")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>强制删除（镜像被实例引用或存在关联容器时需要）</summary>
    public bool Force { get; set; }

    /// <summary>不删除未被引用的父镜像</summary>
    public bool NoPrune { get; set; }
}

public class DockerImageTagRequest
{
    [Required(ErrorMessage = "源镜像不能为空")]
    public string Source { get; set; } = string.Empty;

    [Required(ErrorMessage = "目标镜像不能为空")]
    public string Target { get; set; } = string.Empty;
}

/// <summary>
/// 拉取任务状态
/// </summary>
public class DockerPullTaskStatus
{
    public string TaskId { get; set; } = string.Empty;

    /// <summary>pending / processing / success / error</summary>
    public string Status { get; set; } = "pending";

    public int Progress { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>实际拉取的镜像</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>最近的输出日志</summary>
    public List<string> Logs { get; set; } = new();
}

/// <summary>
/// 镜像操作结果
/// </summary>
public class DockerOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>docker 的原始输出</summary>
    public string? Output { get; set; }
}

public class DockerImageCheckUpdateRequest
{
    /// <summary>要检测的镜像列表（为空时检测所有有标签的镜像）</summary>
    public List<string>? References { get; set; }
}

public class DockerImageCheckUpdateItem
{
    public string Reference { get; set; } = string.Empty;
    public bool HasUpdate { get; set; }
    public string? LocalDigest { get; set; }
    public string? RemoteDigest { get; set; }
    public string Status { get; set; } = "unknown"; // "upToDate" | "hasUpdate" | "error"
    public string? Message { get; set; }
}
