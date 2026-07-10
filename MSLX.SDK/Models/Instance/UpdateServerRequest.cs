using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Instance;

public class UpdateServerRequest : IValidatableObject
{
    [Required(ErrorMessage = "实例ID (ID) 不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "实例ID 必须大于 0")]
    public int ID { get; set; }

    [Required(ErrorMessage = "服务器名称 (Name) 不能为空")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "基础路径 (Base) 不能为空")]
    public required string Base { get; set; }

    [Required(ErrorMessage = "Java环境路径 (Java) 不能为空")]
    public required string Java { get; set; }

    // 这里通常指的是当前的核心文件名，或者更新后的目标文件名
    [Required(ErrorMessage = "核心文件名 (Core) 不能为空")]
    public required string Core { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "最小内存 (MinM) 不能小于 0")]
    public int? MinM { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "最大内存 (MaxM) 不能小于 0")]
    public int? MaxM { get; set; }

    public string? Args { get; set; }

    [Range(1, 300, ErrorMessage = "强制退出延迟时间不能小于 1s 或大于 300s")]
    public int ForceExitDelay { get; set; } = 10;

    public string StopCommand { get; set; } = "stop";

    public string YggdrasilApiAddr { get; set; } = "";

    [Range(1, 100, ErrorMessage = "备份数量限制必须在 1-100 之间")]
    public int BackupMaxCount { get; set; } = 20;

    [Range(5, int.MaxValue, ErrorMessage = "备份间隔必须大于 5")]
    public int BackupDelay { get; set; } = 10;

    public string BackupPath { get; set; } = "MSLX://Backup/Instance";
    public bool AllowOriginASCIIColors { get; set; } = true;
    public bool MonitorPlayers { get; set; } = true;
    public bool AutoRestart { get; set; } = false;
    public bool ForceAutoRestart { get; set; } = true;
    public bool RunOnStartup { get; set; } = false;
    public bool IgnoreEula { get; set; } = false;
    public bool ForceJvmUTF8 { get; set; } = false;

    // 两个编码 暂时仅支持 utf-8 和 gbk

    [RegularExpression(@"^(?i)(utf-8|gbk)$", ErrorMessage = "输入编码 (InputEncoding) 仅支持 'utf-8' 或 'gbk'")]
    public string InputEncoding { get; set; } = "utf-8";

    [RegularExpression(@"^(?i)(utf-8|gbk)$", ErrorMessage = "输出编码 (OutputEncoding) 仅支持 'utf-8' 或 'gbk'")]
    public string OutputEncoding { get; set; } = "utf-8";

    [RegularExpression(@"^(?i)(utf-8|utf-8-bom|gbk)$", ErrorMessage = "文件编码 (FileEncoding) 仅支持 'utf-8', 'utf-8-bom' 或 'gbk'")]
    public string FileEncoding { get; set; } = "utf-8";

    public string ServerPropertiesPath { get; set; } = "server.properties";
    public string PluginsPath { get; set; } = "plugins";
    public string ModsPath { get; set; } = "mods";
    public string WorldPath { get; set; } = "world";
    public string RegionPath { get; set; } = "region";

    // ====== Docker 相关配置参数 ======
    public string DockerImage { get; set; } = "MSLX://DockerImage/Java/25";

    public string DockerWorkingDir { get; set; } = "/mslx-data";

    // 允许为空，若不为空则粗略匹配 路径:路径 的挂载格式
    [RegularExpression(@"^([^:]+:[^:]+)(,[^:]+:[^:]+)*$", ErrorMessage = "挂载目录 (DockerVolumes) 格式不正确，应为 '/宿主机路径:/容器内路径'，多个用逗号隔开")]
    public string? DockerVolumes { get; set; }

    // 允许为空，若不为空则匹配 Key=Value 的环境变量格式
    [RegularExpression(@"^([^=]+=[^,]*)(,[^=]+=[^,]*)*$", ErrorMessage = "环境变量 (DockerEnvVars) 格式不正确，应为 'KEY=VALUE'，多个用逗号隔开")]
    public string? DockerEnvVars { get; set; }

    public string? DockerNetworkMode { get; set; } = "bridge";

    public string? DockerNetworkAlias { get; set; }

    // 允许为空，或为单一的 "0"（代表host），或者匹配 宿主机端口:容器端口
    [RegularExpression(@"^(0|^([0-9]+:[0-9]+)(,[0-9]+:[0-9]+)*)$", ErrorMessage = "开放端口 (DockerPorts) 格式不正确，应为 '0' 或 '宿主机端口:容器端口'，多个用逗号隔开")]
    public string? DockerPorts { get; set; }

    [Range(1, 100, ErrorMessage = "CPU 使用率限制 (DockerCpuPercentage) 必须在 1 到 100 之间")]
    public int? DockerCpuPercentage { get; set; }

    // 指定CPU核心，如 "0" 或 "0,1" 或 "0-3"
    [RegularExpression(@"^[0-9,-]+$", ErrorMessage = "指定 CPU 核心 (DockerCpuCores) 格式不正确，仅支持数字、逗号或连字符，如 '0,1' 或 '0-3'")]
    public string? DockerCpuCores { get; set; }

    [Range(4, 512000, ErrorMessage = "容器最大内存 (DockerMaxMemoryMb) 不能小于 4MB 或大于 500GB")]
    public int? DockerMaxMemoryMb { get; set; }

    [Range(0, 512000, ErrorMessage = "容器最大交换内存 (DockerMaxSwapMb) 校验不正确")]
    public int? DockerMaxSwapMb { get; set; }

    // 限额如 "10g" 或 "500m"
    [RegularExpression(@"^[0-9]+[gGmMkK]$", ErrorMessage = "最大存储空间 (DockerMaxStorage) 格式不正确，应以 m 或 g 结尾，如 '10g'")]
    public string? DockerMaxStorage { get; set; }

    [RegularExpression(@"^[0-9]+[bBmMkK](bps)?$", ErrorMessage = "上传速度限制格式不正确，如 '1mb' 或 '500kb'")]
    public string? DockerUploadRate { get; set; }

    [RegularExpression(@"^[0-9]+[bBmMkK](bps)?$", ErrorMessage = "下载速度限制格式不正确，如 '1mb' 或 '500kb'")]
    public string? DockerDownloadRate { get; set; }

    public string? DockerExtraArgs { get; set; }

    public string? DockerExtraHosts { get; set; } // 格式: host.mslx.internal:host-gateway

    // 更新的可选参数

    // 本地上传服务端 Key
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "CoreFileKey 格式不正确 (32位MD5)")]
    public string? CoreFileKey { get; set; }

    // 远程下载服务端 URL
    [RegularExpression(@"^https?://.+", ErrorMessage = "核心下载地址 (CoreUrl) 必须以 http:// 或 https:// 开头")]
    public string? CoreUrl { get; set; }

    // SHA256 校验
    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "CoreSha256 必须是有效的 64 位十六进制字符串")]
    public string? CoreSha256 { get; set; }

    /// <summary>
    /// 自定义逻辑验证
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 冲突
        if (!string.IsNullOrWhiteSpace(CoreFileKey) && !string.IsNullOrWhiteSpace(CoreUrl))
        {
            yield return new ValidationResult(
                "冲突：不能同时提供 '核心文件上传 (CoreFileKey)' 和 '远程下载地址 (CoreUrl)'，请只选择一种更新方式。",
                new[] { nameof(CoreFileKey), nameof(CoreUrl) }
            );
        }

        // 最大内存不能小于最小内存
        if (MinM.HasValue && MaxM.HasValue && MaxM < MinM)
        {
            yield return new ValidationResult(
                $"逻辑错误：最大内存 ({MaxM}) 不能小于 最小内存 ({MinM})。",
                new[] { nameof(MaxM), nameof(MinM) }
            );
        }

        foreach (var result in ValidateRelativeInstancePath(ServerPropertiesPath, "server.properties", "server.properties 路径必须是实例目录内的相对路径", nameof(ServerPropertiesPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径", nameof(PluginsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径", nameof(ModsPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(WorldPath, "world", "地图目录路径必须是实例目录内的相对路径", nameof(WorldPath)))
            yield return result;
        foreach (var result in ValidateRelativeInstancePath(RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径", nameof(RegionPath)))
            yield return result;

        // docker参数验证
        bool isDockerMode = "docker-java".Equals(Java, StringComparison.OrdinalIgnoreCase) ||
                            "docker-custom".Equals(Java, StringComparison.OrdinalIgnoreCase);

        if (isDockerMode)
        {
            if (!string.IsNullOrWhiteSpace(DockerWorkingDir) && !DockerWorkingDir.StartsWith("/"))
            {
                yield return new ValidationResult(
                    "Docker 工作目录 (DockerWorkingDir) 必须是容器内部的绝对路径，且以 '/' 开头。",
                    new[] { nameof(DockerWorkingDir) }
                );
            }

            // 网络别名验证
            if (!string.IsNullOrWhiteSpace(DockerNetworkAlias))
            {
                string normMode = DockerNetworkMode?.Trim().ToLower() ?? "bridge";
                if (normMode == "bridge" || normMode == "host" || normMode == "none")
                {
                    yield return new ValidationResult(
                        $"规范错误：网络别名 (DockerNetworkAlias) 仅支持在用户自定义网络中配置。当前网络模式为 '{normMode}'，属于 Docker 内置网络，强行配置会导致容器无法拉起。",
                        new[] { nameof(DockerNetworkAlias), nameof(DockerNetworkMode) }
                    );
                }
            }

            // 限制交换内存不能小于最大物理内存
            if (DockerMaxMemoryMb.HasValue && DockerMaxSwapMb.HasValue)
            {
                // 0 不用swap
                if (DockerMaxSwapMb.Value == 0)
                {
                }
                else if (DockerMaxSwapMb.Value != -1 && DockerMaxSwapMb.Value < DockerMaxMemoryMb.Value)
                {
                    yield return new ValidationResult(
                        $"逻辑错误：Docker 容器的交换内存限制 ({DockerMaxSwapMb}MB) 作为总内存上限，不能小于 最大物理内存限制 ({DockerMaxMemoryMb}MB)。" +
                        $"如果您想彻底禁用 Swap，请将交换内存的值设置为与物理内存完全一致（即填入 {DockerMaxMemoryMb}）。",
                        new[] { nameof(DockerMaxSwapMb), nameof(DockerMaxMemoryMb) }
                    );
                }
            }
        }
        else
        {
            // 如果 Java 选的是普通的本地路径，但用户却填了 Docker 专属的高级参数，予以友情拦截
            if (!string.IsNullOrWhiteSpace(DockerNetworkAlias) || !string.IsNullOrWhiteSpace(DockerPorts) || DockerMaxMemoryMb.HasValue)
            {
                yield return new ValidationResult(
                    "提示：当前服务器未开启 Docker 运行运行模式 (Java 属性未选择 docker-java/docker-custom)，请勿配置 Docker 专属限制参数。",
                    new[] { nameof(Java) }
                );
            }
        }
    }

    private static IEnumerable<ValidationResult> ValidateRelativeInstancePath(string? path, string defaultPath, string message, string memberName)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? defaultPath
            : path.Trim().Replace('\\', '/');
        var isWindowsAbsolutePath = normalizedPath.Length >= 3 &&
                                    char.IsLetter(normalizedPath[0]) &&
                                    normalizedPath[1] == ':' &&
                                    normalizedPath[2] == '/';
        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var hasInvalidSegment = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".." || segment.IndexOfAny(invalidFileNameChars) >= 0);

        if (Path.IsPathRooted(normalizedPath) || isWindowsAbsolutePath || hasInvalidSegment)
        {
            yield return new ValidationResult(message, new[] { memberName });
        }
    }
}
