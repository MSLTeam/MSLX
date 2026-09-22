using MSLX.Daemon.Services.InstanceServices;

namespace MSLX.Tests;

public class InstanceConsoleServiceTests
{
    private readonly InstanceConsoleService _console = new();

    [Fact]
    public void AppendLog_SingleLine_CanBeReadBack()
    {
        var context = new ServerContext();

        _console.AppendLog(context, "hello");

        Assert.Equal(new[] { "hello" }, _console.GetLogs(context));
    }

    [Fact]
    public void AppendLog_MultipleLines_PreservesOrder()
    {
        var context = new ServerContext();
        _console.AppendLog(context, "line1");
        _console.AppendLog(context, "line2");
        _console.AppendLog(context, "line3");

        Assert.Equal(new[] { "line1", "line2", "line3" }, _console.GetLogs(context));
    }

    [Fact]
    public void AppendLog_ExceedingCapacity_DropsOldestLines()
    {
        var context = new ServerContext();
        for (int i = 0; i < InstanceConsoleService.MaxLogLines + 50; i++)
        {
            _console.AppendLog(context, $"line-{i}");
        }

        var logs = _console.GetLogs(context);

        Assert.Equal(InstanceConsoleService.MaxLogLines, logs.Count);
        Assert.Equal("line-50", logs.First()); // 最旧的 50 行已被丢弃
        Assert.Equal($"line-{InstanceConsoleService.MaxLogLines + 49}", logs.Last());
    }

    [Fact]
    public void AppendPtyHistory_ExceedingCapacity_DropsOldestChunks()
    {
        var context = new ServerContext();
        for (int i = 0; i < InstanceConsoleService.MaxPtyHistoryChunks + 10; i++)
        {
            _console.AppendPtyHistory(context, $"chunk-{i}");
        }

        var history = _console.GetPtyHistory(context);

        Assert.Equal(InstanceConsoleService.MaxPtyHistoryChunks, history.Count);
        Assert.Equal("chunk-10", history.First());
    }

    [Fact]
    public void GetLogs_NullContext_ReturnsEmptyList()
    {
        Assert.Empty(_console.GetLogs(null));
    }

    [Fact]
    public void GetPtyHistory_NullContext_ReturnsEmptyList()
    {
        Assert.Empty(_console.GetPtyHistory(null));
    }

    [Fact]
    public void AppendLog_ConcurrentWrites_DoNotThrowAndStayWithinCapacity()
    {
        var context = new ServerContext();

        Parallel.For(0, 5000, i => _console.AppendLog(context, $"line-{i}"));

        var logs = _console.GetLogs(context);
        Assert.True(logs.Count <= InstanceConsoleService.MaxLogLines);
        Assert.True(logs.Count > 0);
    }
}
