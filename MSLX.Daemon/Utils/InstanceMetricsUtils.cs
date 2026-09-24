using System.Text.RegularExpressions;

namespace MSLX.Daemon.Utils;

/// <summary>
/// 实例资源指标的纯计算逻辑（Docker stats 行解析、CPU 百分比换算），
/// </summary>
public static class InstanceMetricsUtils
{
    private static readonly Regex MemUsageRegex =
        new(@"(?<value>[\d\.]+)\s*(?<unit>[a-zA-Z]+)?", RegexOptions.Compiled);

    /// <summary>
    /// 解析 docker stats --format "{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}" 的单行输出。
    /// 行格式示例: "mslx-container-1|150.35%|345.2MiB / 8GiB"。
    /// 返回实例 ID、容器名、原始 CPU 百分比与内存字节数；格式非法时返回 null。
    /// </summary>
    public static (uint instanceId, string containerName, double rawCpuPercent, long memoryBytes)?
        ParseDockerStatsLine(string line)
    {
        string[] parts = line.Split('|');
        if (parts.Length < 3) return null;

        string containerName = parts[0].Trim();

        // 提取 instanceId
        if (!uint.TryParse(containerName.Replace("mslx-container-", ""), out uint instanceId))
            return null;

        // 解析原始占用
        string cpuStr = parts[1].Replace("%", "").Trim();
        double.TryParse(cpuStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double rawCpu);

        // 解析内存（"/" 前为实际占用，后为上限）
        long memoryBytes = 0;
        string memUsagePart = parts[2].Split('/')[0].Trim();
        var match = MemUsageRegex.Match(memUsagePart);

        if (match.Success)
        {
            // 正则可能匹配出多小数点的值（如 "1.2.3"），解析失败按格式非法处理
            if (!double.TryParse(match.Groups["value"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                return null;

            string unit = match.Groups["unit"].Value.ToUpper();

            memoryBytes = unit switch
            {
                "GIB" or "GB" => (long)(val * 1024 * 1024 * 1024),
                "MIB" or "MB" => (long)(val * 1024 * 1024),
                "KIB" or "KB" => (long)(val * 1024),
                "B" => (long)val,
                _ => (long)(val * 1024 * 1024) // 无单位/未知单位按 MiB 处理（沿用历史行为）
            };
        }

        return (instanceId, containerName, rawCpu, memoryBytes);
    }

    /// <summary>
    /// 将 Docker 原始 CPU 百分比按基准（配置核心数×100）折算并钳位到 0-100。
    /// 基准非正时按 0 处理（防御除零产生 Infinity；正常流程调用方已做回退，不会传入非正值）。
    /// </summary>
    public static double NormalizeDockerCpu(double rawCpuPercent, double baseLimitPercentage)
    {
        if (baseLimitPercentage <= 0) return 0;
        double normalized = (rawCpuPercent / baseLimitPercentage) * 100.0;
        if (normalized > 100.0) normalized = 100.0;
        if (normalized < 0.0) normalized = 0.0;
        return normalized;
    }

    /// <summary>
    /// 由前后两次采样计算原生进程 CPU 百分比：CPU 时间增量 / 墙钟时间增量 / 核心数 × 100，
    /// 钳位到 0-100；墙钟时间增量非正时返回 0。核心数非正时按 1 处理（防御 0/0 产生 NaN）。
    /// </summary>
    public static double ComputeCpuPercent(double cpuTimePassedMs, double timePassedMs, int processorCount)
    {
        if (timePassedMs <= 0) return 0;
        if (processorCount <= 0) processorCount = 1;
        double cpuUsage = (cpuTimePassedMs / timePassedMs) / processorCount * 100;
        if (cpuUsage > 100) cpuUsage = 100;
        if (cpuUsage < 0) cpuUsage = 0;
        return cpuUsage;
    }
}
