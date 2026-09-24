using System.Text;
using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class EncodingUtilsTests
{
    public EncodingUtilsTests()
    {
        // GBK 等非 Unicode 编码需要注册编码提供程序（与 Daemon 启动时的行为一致）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Get_NullOrWhitespace_ReturnsUtf8NoBom(string? name)
    {
        var enc = EncodingUtils.GetEncodingOrDefault(name);
        Assert.IsType<UTF8Encoding>(enc);
        Assert.Empty(enc.GetPreamble());
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf8")]
    [InlineData("UTF-8")]
    [InlineData(" utf-8 ")]
    public void Get_Utf8Variants_ReturnsUtf8NoBom(string name)
    {
        var enc = EncodingUtils.GetEncodingOrDefault(name);
        Assert.IsType<UTF8Encoding>(enc);
        Assert.Empty(enc.GetPreamble());
    }

    [Fact]
    public void Get_Gbk_ReturnsCodePage936()
    {
        var enc = EncodingUtils.GetEncodingOrDefault("gbk");
        Assert.Equal(936, enc.CodePage);
    }

    [Fact]
    public void Get_Utf16_ReturnsUtf16WithBom()
    {
        var enc = EncodingUtils.GetEncodingOrDefault("utf-16");
        Assert.Equal(1200, enc.CodePage);
        Assert.NotEmpty(enc.GetPreamble());
    }

    [Fact]
    public void Get_UnknownEncoding_FallsBackToUtf8NoBom()
    {
        var enc = EncodingUtils.GetEncodingOrDefault("not-a-real-codec");
        Assert.IsType<UTF8Encoding>(enc);
        Assert.Empty(enc.GetPreamble());
    }

    [Fact]
    public void Get_UnknownEncoding_InvokesFallbackCallbackWithOriginalName()
    {
        string? captured = null;
        EncodingUtils.GetEncodingOrDefault("not-a-real-codec", name => captured = name);
        Assert.Equal("not-a-real-codec", captured);
    }

    [Fact]
    public void Get_ValidEncoding_DoesNotInvokeFallbackCallback()
    {
        bool invoked = false;
        EncodingUtils.GetEncodingOrDefault("gbk", _ => invoked = true);
        Assert.False(invoked);
    }
}
