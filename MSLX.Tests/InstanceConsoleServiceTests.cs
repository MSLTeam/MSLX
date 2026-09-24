using MSLX.Daemon.Hubs;
using MSLX.Daemon.Services.InstanceServices;
using MSLX.Tests.Fakes;

namespace MSLX.Tests;

public class InstanceConsoleServiceTests
{
    private readonly FakeHubContext<InstanceConsoleHub> _hub = new();
    private readonly FakeMSLXEvents _events = new();
    private readonly InstanceConsoleService _console;

    public InstanceConsoleServiceTests()
    {
        _console = new InstanceConsoleService(_hub, _events);
    }

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

    #region RecordLog（缓冲 + 广播 + 玩家解析编排）

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordLog_NullOrWhitespaceLine_IsIgnored(string? line)
    {
        var context = new ServerContext();

        _console.RecordLog(1, context, line);

        Assert.Empty(_console.GetLogs(context));
        Assert.Empty(_events.PublishedLogs);
        Assert.Empty(_hub.HubClients.GroupProxy.Sent);
    }

    [Fact]
    public void RecordLog_NormalLine_AppendsPublishesAndBroadcasts()
    {
        var context = new ServerContext();

        _console.RecordLog(1, context, "[12:00:00] Server started");

        Assert.Equal(new[] { "[12:00:00] Server started" }, _console.GetLogs(context));
        Assert.Single(_events.PublishedLogs);
        Assert.Equal("[12:00:00] Server started", _events.PublishedLogs[0].LogLine);
        Assert.Contains(_hub.HubClients.GroupProxy.Sent,
            s => s.Method == "ReceiveLog" && (string?)s.Args[0] == "[12:00:00] Server started");
    }

    [Fact]
    public void RecordLog_PlayerJoinLine_AddsPlayerAndBroadcasts()
    {
        var context = new ServerContext();
        var line = "[12:00:00] [Server thread/INFO]: Steve[/127.0.0.1:25565] logged in with entity id 1";

        _console.RecordLog(1, context, line);

        Assert.True(context.OnlinePlayers.ContainsKey("Steve"));
        Assert.Contains(_hub.HubClients.GroupProxy.Sent,
            s => s.Method == "PlayerJoined" && (string?)s.Args[1] == "Steve");
    }

    [Fact]
    public void RecordLog_PlayerLeftLine_RemovesPlayerAndBroadcasts()
    {
        var context = new ServerContext();
        _console.RecordLog(1, context,
            "[12:00:00] [Server thread/INFO]: Steve[/127.0.0.1:25565] logged in with entity id 1");

        _console.RecordLog(1, context, "[12:05:00] [Server thread/INFO]: Steve lost connection: Disconnected");

        Assert.False(context.OnlinePlayers.ContainsKey("Steve"));
        Assert.Contains(_hub.HubClients.GroupProxy.Sent,
            s => s.Method == "PlayerLeft" && (string?)s.Args[1] == "Steve");
    }

    [Fact]
    public void RecordLog_FakePlayerJoinLine_DoesNotAddPlayer()
    {
        var context = new ServerContext();

        _console.RecordLog(1, context,
            "[12:00:00] [Server thread/INFO]: LocalBot[/127.0.0.1:25565] logged in with entity id 1");

        Assert.Empty(context.OnlinePlayers);
        Assert.DoesNotContain(_hub.HubClients.GroupProxy.Sent, s => s.Method == "PlayerJoined");
    }

    [Fact]
    public void RecordLog_MonitorPlayersDisabled_SkipsPlayerParsingButStillLogs()
    {
        var context = new ServerContext { MonitorPlayers = false };

        _console.RecordLog(1, context,
            "[12:00:00] [Server thread/INFO]: Steve[/127.0.0.1:25565] logged in with entity id 1");

        Assert.Single(_console.GetLogs(context)); // 日志照常记录
        Assert.Empty(context.OnlinePlayers);      // 但不解析玩家
        Assert.DoesNotContain(_hub.HubClients.GroupProxy.Sent, s => s.Method == "PlayerJoined");
    }

    #endregion
}
