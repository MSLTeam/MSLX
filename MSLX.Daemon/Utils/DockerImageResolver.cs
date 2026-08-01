using System.Globalization;
using System.Text.RegularExpressions;
using MSLX.SDK.Models.Docker;

namespace MSLX.Daemon.Utils;

/// <summary>
/// Docker 镜像名相关的解析与校验工具
/// </summary>
public static class DockerImageResolver
{
    /// <summary>伪协议前缀</summary>
    public const string PseudoPrefix = "MSLX://DockerImage/Java/";

    /// <summary>内置运行时镜像仓库</summary>
    public const string RuntimeRepository = "docker.mslmc.cn/xiaoyululu/mslx-runtime";

    /// <summary>内置运行时提供的 Java 版本</summary>
    public static readonly string[] PresetJavaVersions = ["8", "11", "17", "21", "25"];

    // 合法引用：仅允许字母数字与 . _ - : / @ +，且不以 - 开头
    private static readonly Regex ReferencePattern =
        new(@"^[a-zA-Z0-9_][a-zA-Z0-9._:/@+-]*$", RegexOptions.Compiled);

    private static readonly Regex ImageIdPattern =
        new(@"^(sha256:)?[a-f0-9]{12,64}$", RegexOptions.Compiled);

    private static readonly Regex SizePattern =
        new(@"^\s*(\d+(?:\.\d+)?)\s*([a-zA-Z]*)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 是否是 MSLX 内置运行时伪协议
    /// </summary>
    public static bool IsPseudo(string? image)
    {
        return !string.IsNullOrWhiteSpace(image) &&
               image.StartsWith(PseudoPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把伪协议解析为真实镜像地址
    /// </summary>
    public static string Resolve(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return string.Empty;

        var trimmed = image.Trim();
        if (!IsPseudo(trimmed)) return trimmed;

        var javaVer = trimmed[PseudoPrefix.Length..].Trim().TrimEnd('/');
        return string.IsNullOrEmpty(javaVer)
            ? $"{RuntimeRepository}:java21"
            : $"{RuntimeRepository}:java{javaVer}";
    }

    /// <summary>
    /// 是否是内置运行时镜像（伪协议/解析后地址）
    /// </summary>
    public static bool IsMslxRuntime(string? image)
    {
        if (string.IsNullOrWhiteSpace(image)) return false;
        return IsPseudo(image) ||
               image.Trim().StartsWith(RuntimeRepository, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 内置运行时镜像清单
    /// </summary>
    public static List<DockerPresetImage> GetPresetImages()
    {
        return PresetJavaVersions.Select(ver => new DockerPresetImage
        {
            Pseudo = $"{PseudoPrefix}{ver}",
            Image = $"{RuntimeRepository}:java{ver}",
            Label = $"MSLX 运行时 [Java {ver}]"
        }).ToList();
    }

    /// <summary>
    /// 校验镜像引用是否安全合法
    /// </summary>
    public static bool IsValidReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;

        var value = reference.Trim();
        if (value.Length > 512) return false;

        return ReferencePattern.IsMatch(value);
    }

    /// <summary>
    /// 是否形似镜像 ID
    /// </summary>
    public static bool LooksLikeImageId(string? reference)
    {
        return !string.IsNullOrWhiteSpace(reference) && ImageIdPattern.IsMatch(reference.Trim());
    }

    /// <summary>
    /// 补全默认 tag：无 tag / 无 digest 时追加 :latest
    /// </summary>
    public static string NormalizeReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;

        var value = reference.Trim();

        // digest 引用与镜像 ID 不补 tag
        if (value.Contains('@') || LooksLikeImageId(value)) return value;

        var lastSlash = value.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? value[(lastSlash + 1)..] : value;

        return lastSegment.Contains(':') ? value : $"{value}:latest";
    }

    /// <summary>
    /// 把格式化的单位转换为字节数
    /// </summary>
    public static long? ParseSizeToBytes(string? size)
    {
        if (string.IsNullOrWhiteSpace(size)) return null;

        var match = SizePattern.Match(size);
        if (!match.Success) return null;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var number))
        {
            return null;
        }

        var unit = match.Groups[2].Value.ToLowerInvariant();
        var isBinary = unit.Contains('i'); // KiB / MiB / GiB
        double @base = isBinary ? 1024 : 1000;

        var exponent = unit.TrimEnd('b').TrimEnd('i') switch
        {
            "" => 0,
            "k" => 1,
            "m" => 2,
            "g" => 3,
            "t" => 4,
            "p" => 5,
            _ => -1
        };

        if (exponent < 0) return null;

        return (long)(number * Math.Pow(@base, exponent));
    }
}
