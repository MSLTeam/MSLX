using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class InstanceMetricsUtilsTests
{
    #region ParseDockerStatsLine

    [Fact]
    public void Parse_ValidLine_ReturnsAllFields()
    {
        var result = InstanceMetricsUtils.ParseDockerStatsLine("mslx-container-1|150.35%|345.2MiB / 8GiB");

        Assert.NotNull(result);
        Assert.Equal(1u, result.Value.instanceId);
        Assert.Equal("mslx-container-1", result.Value.containerName);
        Assert.Equal(150.35, result.Value.rawCpuPercent, 2);
        Assert.Equal((long)(345.2 * 1024 * 1024), result.Value.memoryBytes);
    }

    [Fact]
    public void Parse_ZeroUsage_ReturnsZeros()
    {
        var result = InstanceMetricsUtils.ParseDockerStatsLine("mslx-container-3|0.00%|0B / 8GiB");

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.rawCpuPercent);
        Assert.Equal(0, result.Value.memoryBytes);
    }

    [Theory]
    [InlineData("mslx-container-2|12.5%|1GiB / 2GiB", 1L * 1024 * 1024 * 1024)]
    [InlineData("mslx-container-2|12.5%|512KiB / 8GiB", 512L * 1024)]
    [InlineData("mslx-container-2|12.5%|128MB / 2GB", 128L * 1024 * 1024)]
    [InlineData("mslx-container-2|12.5%|256 / 8GiB", 256L * 1024 * 1024)] // 无单位按 MiB（历史行为）
    public void Parse_MemoryUnits_ConvertedToBytes(string line, long expectedBytes)
    {
        var result = InstanceMetricsUtils.ParseDockerStatsLine(line);

        Assert.NotNull(result);
        Assert.Equal(expectedBytes, result.Value.memoryBytes);
    }

    [Theory]
    [InlineData("garbage")]                    // 无分隔符
    [InlineData("mslx-container-1|10%")]       // 列数不足
    [InlineData("abc|10%|1MiB / 2GiB")]        // 容器名不含实例 ID
    [InlineData("mslx-container-1|10%|1.2.3MiB / 8GiB")] // 多小数点数值：不得抛异常
    [InlineData("mslx-container-1|10%|..MiB / 8GiB")]    // 纯小数点：不得抛异常
    [InlineData("mslx-container-1|10%|1..2GiB / 8GiB")]  // 值中夹小数点：不得抛异常
    public void Parse_MalformedLine_ReturnsNull(string line)
    {
        Assert.Null(InstanceMetricsUtils.ParseDockerStatsLine(line));
    }

    #endregion

    #region NormalizeDockerCpu

    [Fact]
    public void Normalize_NormalValue_ScalesByBase()
    {
        Assert.Equal(75.175, InstanceMetricsUtils.NormalizeDockerCpu(150.35, 200), 2);
    }

    [Fact]
    public void Normalize_OverCap_ClampedTo100()
    {
        Assert.Equal(100.0, InstanceMetricsUtils.NormalizeDockerCpu(500, 100));
    }

    [Fact]
    public void Normalize_Negative_ClampedTo0()
    {
        Assert.Equal(0.0, InstanceMetricsUtils.NormalizeDockerCpu(-5, 100));
    }

    #endregion

    #region ComputeCpuPercent

    [Fact]
    public void Compute_NormalSample_ReturnsPercentage()
    {
        // 4 核机器，1 秒墙钟内消耗 500ms CPU → 12.5%
        Assert.Equal(12.5, InstanceMetricsUtils.ComputeCpuPercent(500, 1000, 4), 2);
    }

    [Fact]
    public void Compute_ZeroTimePassed_ReturnsZero()
    {
        Assert.Equal(0, InstanceMetricsUtils.ComputeCpuPercent(500, 0, 4));
        Assert.Equal(0, InstanceMetricsUtils.ComputeCpuPercent(500, -1, 4));
    }

    [Fact]
    public void Compute_OverCap_ClampedTo100()
    {
        Assert.Equal(100.0, InstanceMetricsUtils.ComputeCpuPercent(100000, 1000, 1));
    }

    [Fact]
    public void Compute_NegativeCpuDelta_ClampedTo0()
    {
        Assert.Equal(0.0, InstanceMetricsUtils.ComputeCpuPercent(-100, 1000, 4));
    }

    #endregion
}
