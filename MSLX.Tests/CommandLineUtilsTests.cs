using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class CommandLineUtilsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_NullOrWhitespace_ReturnsEmpty(string? input)
    {
        Assert.Empty(CommandLineUtils.SplitCommandLineArgs(input));
    }

    [Fact]
    public void Split_SimpleArgs_SplitsOnWhitespace()
    {
        var result = CommandLineUtils.SplitCommandLineArgs("java -jar server.jar");
        Assert.Equal(new[] { "java", "-jar", "server.jar" }, result);
    }

    [Fact]
    public void Split_QuotedArgWithSpaces_KeepsAsSingleArg()
    {
        var result = CommandLineUtils.SplitCommandLineArgs("java -jar \"my server.jar\" nogui");
        Assert.Equal(new[] { "java", "-jar", "my server.jar", "nogui" }, result);
    }

    [Fact]
    public void Split_MultipleSpacesAndTabs_CollapsesSeparators()
    {
        var result = CommandLineUtils.SplitCommandLineArgs("java   -Xmx2G\t-jar   server.jar");
        Assert.Equal(new[] { "java", "-Xmx2G", "-jar", "server.jar" }, result);
    }

    [Fact]
    public void Split_QuoteAttachedToArg_PreservesPrefix()
    {
        var result = CommandLineUtils.SplitCommandLineArgs("--name=\"hello world\" --force");
        Assert.Equal(new[] { "--name=hello world", "--force" }, result);
    }

    [Fact]
    public void Split_EmptyQuotedPair_IsDropped()
    {
        // 空引号对不产生参数（记录现有行为）
        var result = CommandLineUtils.SplitCommandLineArgs("a \"\" b");
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void Split_UnclosedQuote_ConsumesToEndOfLine()
    {
        // 未闭合的引号让剩余内容（含空格）成为一个参数（记录现有行为）
        var result = CommandLineUtils.SplitCommandLineArgs("java \"-jar x");
        Assert.Equal(new[] { "java", "-jar x" }, result);
    }

    [Fact]
    public void Split_QuotedPathWithChineseAndSpaces_Works()
    {
        var result = CommandLineUtils.SplitCommandLineArgs("\"C:\\游戏 目录\\server.jar\" -Xmx4G");
        Assert.Equal(new[] { "C:\\游戏 目录\\server.jar", "-Xmx4G" }, result);
    }
}
