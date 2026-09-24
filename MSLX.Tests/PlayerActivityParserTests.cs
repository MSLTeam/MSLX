using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class PlayerActivityParserTests
{
    [Fact]
    public void Parse_PlayerJoined_ReturnsJoinWithNameAndIp()
    {
        var line = "[12:00:00] [Server thread/INFO]: Steve[/127.0.0.1:25565] logged in with entity id 123 at (100.5, 64.0, -200.5)";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Joined, activity.Type);
        Assert.Equal("Steve", activity.PlayerName);
        Assert.Equal("127.0.0.1", activity.PlayerIp);
    }

    [Fact]
    public void Parse_PlayerLeft_ReturnsLeftWithName()
    {
        var line = "[12:05:00] [Server thread/INFO]: Steve lost connection: Disconnected";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Left, activity.Type);
        Assert.Equal("Steve", activity.PlayerName);
        Assert.Null(activity.PlayerIp);
    }

    [Fact]
    public void Parse_JoinedWithAnsiColors_StripsColorCodes()
    {
        var line = "\u001b[32m[12:00:00] [Server thread/INFO]: Alex[/10.0.0.2:50000] logged in with entity id 7\u001b[0m";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Joined, activity.Type);
        Assert.Equal("Alex", activity.PlayerName);
        Assert.Equal("10.0.0.2", activity.PlayerIp);
    }

    [Theory]
    // 名称含 local（忽略大小写）
    [InlineData("[12:00:00] [Server thread/INFO]: LocalBot[/127.0.0.1:25565] logged in with entity id 1")]
    [InlineData("[12:00:00] [Server thread/INFO]: LOCAL[/127.0.0.1:25565] logged in with entity id 1")]
    // IP 含 local（本地假人无真实 IP）
    [InlineData("[12:00:00] [Server thread/INFO]: Bot[local] logged in with entity id 1")]
    public void Parse_FakePlayerJoined_IsFilteredOut(string line)
    {
        Assert.Equal(PlayerActivityType.None, PlayerActivityParser.Parse(line).Type);
    }

    [Fact]
    public void Parse_NameWithBracketSuffix_BracketContentIsCapturedAsIp()
    {
        // 记录现有行为：正则会跨越方括号匹配到 " logged in" 前的最后一个 ]，
        // "Bot[fake]" 这类名字会被解析为玩家 "Bot" + IP "fake][/127.0.0.1"，
        // 由于二者都不含完整的 [..] 对或 local，不会被假人过滤器拦截（潜在缺陷，此处仅固化现状）
        var line = "[12:00:00] [Server thread/INFO]: Bot[fake][/127.0.0.1:25565] logged in with entity id 1";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Joined, activity.Type);
        Assert.Equal("Bot", activity.PlayerName);
        Assert.Equal("fake][/127.0.0.1", activity.PlayerIp);
    }

    [Fact]
    public void Parse_FakePlayerLeft_IsFilteredOut()
    {
        var line = "[12:05:00] [Server thread/INFO]: LocalBot lost connection: Disconnected";
        Assert.Equal(PlayerActivityType.None, PlayerActivityParser.Parse(line).Type);
    }

    [Theory]
    [InlineData("[12:00:00] [Server thread/INFO]: Starting minecraft server version 1.20.1")]
    [InlineData("[12:00:00] [Server thread/INFO]: Steve joined the game")]
    [InlineData("")]
    public void Parse_UnrelatedLine_ReturnsNone(string line)
    {
        Assert.Equal(PlayerActivityType.None, PlayerActivityParser.Parse(line).Type);
    }

    [Fact]
    public void Parse_JoinedIpWithoutLeadingSlash_StillParses()
    {
        var line = "[12:00:00] [Server thread/INFO]: Steve[127.0.0.1:25565] logged in with entity id 1";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Joined, activity.Type);
        Assert.Equal("127.0.0.1", activity.PlayerIp);
    }

    [Fact]
    public void Parse_JoinedPlayerNameWithSpaces_IsTrimmed()
    {
        var line = "[12:00:00] [Server thread/INFO]:  Steve_One [/127.0.0.1:25565] logged in with entity id 1";
        var activity = PlayerActivityParser.Parse(line);

        Assert.Equal(PlayerActivityType.Joined, activity.Type);
        Assert.Equal("Steve_One", activity.PlayerName);
    }

    [Theory]
    [InlineData("Bot[fake]", true)]
    [InlineData("local", true)]
    [InlineData("MyLOCALPlayer", true)]
    [InlineData("Steve", false)]
    [InlineData("127.0.0.1", false)]
    public void IsFakePlayer_MatchesBracketsOrLocal(string input, bool expected)
    {
        Assert.Equal(expected, PlayerActivityParser.IsFakePlayer(input));
    }
}
