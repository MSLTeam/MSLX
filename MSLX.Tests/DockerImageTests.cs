using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;

namespace MSLX.Tests;

public class DockerImageResolverTests
{
    [Theory]
    [InlineData("MSLX://DockerImage/Java/21", "docker.mslmc.cn/xiaoyululu/mslx-runtime:java21")]
    [InlineData("MSLX://DockerImage/Java/8", "docker.mslmc.cn/xiaoyululu/mslx-runtime:java8")]
    [InlineData("mslx://dockerimage/java/25", "docker.mslmc.cn/xiaoyululu/mslx-runtime:java25")]
    [InlineData("  MSLX://DockerImage/Java/17  ", "docker.mslmc.cn/xiaoyululu/mslx-runtime:java17")]
    public void Resolve_expands_builtin_runtime_pseudo_protocol(string input, string expected)
    {
        Assert.Equal(expected, DockerImageResolver.Resolve(input));
    }

    [Theory]
    [InlineData("nginx:latest", "nginx:latest")]
    [InlineData("  eclipse-temurin:21-jre  ", "eclipse-temurin:21-jre")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Resolve_keeps_normal_references_untouched(string? input, string expected)
    {
        Assert.Equal(expected, DockerImageResolver.Resolve(input));
    }

    [Fact]
    public void Resolve_falls_back_to_java21_when_version_missing()
    {
        Assert.Equal("docker.mslmc.cn/xiaoyululu/mslx-runtime:java21",
            DockerImageResolver.Resolve("MSLX://DockerImage/Java/"));
    }

    [Theory]
    [InlineData("nginx", "nginx:latest")]
    [InlineData("nginx:1.25", "nginx:1.25")]
    [InlineData("localhost:5000/app", "localhost:5000/app:latest")]
    [InlineData("localhost:5000/app:dev", "localhost:5000/app:dev")]
    [InlineData("registry.example.com:443/team/app", "registry.example.com:443/team/app:latest")]
    [InlineData("nginx@sha256:abcdef0123456789", "nginx@sha256:abcdef0123456789")]
    [InlineData("a1b2c3d4e5f6", "a1b2c3d4e5f6")]
    public void NormalizeReference_appends_latest_only_when_needed(string input, string expected)
    {
        Assert.Equal(expected, DockerImageResolver.NormalizeReference(input));
    }

    [Theory]
    [InlineData("nginx:latest")]
    [InlineData("docker.mslmc.cn/xiaoyululu/mslx-runtime:java21")]
    [InlineData("registry.example.com:5000/team/app@sha256:0123456789abcdef")]
    [InlineData("app:1.0.0-beta+build1")]
    public void IsValidReference_accepts_normal_references(string input)
    {
        Assert.True(DockerImageResolver.IsValidReference(input));
    }

    [Theory]
    [InlineData("nginx; rm -rf /")]
    [InlineData("nginx && whoami")]
    [InlineData("nginx | cat")]
    [InlineData("$(id)")]
    [InlineData("`id`")]
    [InlineData("nginx latest")]
    [InlineData("--privileged")]
    [InlineData("nginx\nrm -rf /")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValidReference_rejects_dangerous_or_empty_input(string? input)
    {
        Assert.False(DockerImageResolver.IsValidReference(input));
    }

    [Fact]
    public void IsValidReference_rejects_overly_long_input()
    {
        Assert.False(DockerImageResolver.IsValidReference(new string('a', 513)));
    }

    [Theory]
    [InlineData("100B", 100L)]
    [InlineData("1kB", 1000L)]
    [InlineData("1.2GB", 1_200_000_000L)]
    [InlineData("500MB", 500_000_000L)]
    [InlineData(" 2 TB ", 2_000_000_000_000L)]
    [InlineData("1KiB", 1024L)]
    [InlineData("1MiB", 1_048_576L)]
    public void ParseSizeToBytes_understands_docker_size_output(string input, long expected)
    {
        Assert.Equal(expected, DockerImageResolver.ParseSizeToBytes(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("N/A")]
    [InlineData("abc")]
    [InlineData("12XB")]
    public void ParseSizeToBytes_returns_null_for_unparsable_values(string? input)
    {
        Assert.Null(DockerImageResolver.ParseSizeToBytes(input));
    }

    [Theory]
    [InlineData("MSLX://DockerImage/Java/21", true)]
    [InlineData("docker.mslmc.cn/xiaoyululu/mslx-runtime:java21", true)]
    [InlineData("nginx:latest", false)]
    [InlineData(null, false)]
    public void IsMslxRuntime_detects_builtin_runtime(string? input, bool expected)
    {
        Assert.Equal(expected, DockerImageResolver.IsMslxRuntime(input));
    }

    [Fact]
    public void GetPresetImages_covers_all_supported_java_versions()
    {
        var presets = DockerImageResolver.GetPresetImages();

        Assert.Equal(DockerImageResolver.PresetJavaVersions.Length, presets.Count);
        Assert.All(presets, p => Assert.Equal(DockerImageResolver.Resolve(p.Pseudo), p.Image));
    }
}

public class DockerImageListParsingTests
{
    [Fact]
    public void ParseImageList_reads_tagged_and_dangling_images()
    {
        const string stdout = """
                              {"Containers":"N/A","CreatedAt":"2026-05-01 10:00:00 +0800 CST","CreatedSince":"2 months ago","Digest":"<none>","ID":"sha256:1111111111112222222222223333333333334444444444445555555555556666","Repository":"nginx","SharedSize":"N/A","Size":"142MB","Tag":"latest","UniqueSize":"N/A","VirtualSize":"142.3MB"}
                              {"Containers":"N/A","CreatedAt":"2026-04-01 09:00:00 +0800 CST","CreatedSince":"3 months ago","Digest":"<none>","ID":"sha256:aaaaaaaaaaaabbbbbbbbbbbbccccccccccccddddddddddddeeeeeeeeeeeeffff","Repository":"<none>","SharedSize":"N/A","Size":"1.2GB","Tag":"<none>","UniqueSize":"N/A","VirtualSize":"1.2GB"}
                              """;

        var images = DockerService.ParseImageList(stdout);

        Assert.Equal(2, images.Count);

        var nginx = images[0];
        Assert.Equal("nginx:latest", nginx.Reference);
        Assert.Equal("111111111111", nginx.ShortId);
        Assert.Equal(142_000_000L, nginx.SizeBytes);
        Assert.False(nginx.IsDangling);
        Assert.Null(nginx.Digest);

        var dangling = images[1];
        Assert.True(dangling.IsDangling);
        Assert.Equal(dangling.ImageId, dangling.Reference);
        Assert.Equal(1_200_000_000L, dangling.SizeBytes);
    }

    [Fact]
    public void ParseImageList_marks_builtin_runtime_images()
    {
        const string stdout =
            """{"CreatedAt":"2026-06-01 10:00:00 +0800 CST","Digest":"sha256:1234","ID":"sha256:777777777777","Repository":"docker.mslmc.cn/xiaoyululu/mslx-runtime","Size":"320MB","Tag":"java21"}""";

        var image = Assert.Single(DockerService.ParseImageList(stdout));

        Assert.True(image.IsMslxRuntime);
        Assert.Equal("docker.mslmc.cn/xiaoyululu/mslx-runtime:java21", image.Reference);
        Assert.Equal("sha256:1234", image.Digest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json")]
    public void ParseImageList_tolerates_empty_or_malformed_output(string? stdout)
    {
        Assert.Empty(DockerService.ParseImageList(stdout));
    }

    [Fact]
    public void ParseImageList_skips_broken_lines_but_keeps_valid_ones()
    {
        const string stdout = """
                              {"broken json
                              {"CreatedAt":"2026-06-01 10:00:00 +0800 CST","ID":"sha256:888888888888","Repository":"redis","Size":"40MB","Tag":"7"}
                              """;

        var image = Assert.Single(DockerService.ParseImageList(stdout));
        Assert.Equal("redis:7", image.Reference);
    }
}
