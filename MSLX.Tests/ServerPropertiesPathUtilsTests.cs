using MSLX.Daemon.Utils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;

namespace MSLX.Tests;

public class ServerPropertiesPathUtilsTests
{
    [Theory]
    [InlineData(null, "server.properties")]
    [InlineData("", "server.properties")]
    [InlineData("   ", "server.properties")]
    [InlineData("server.properties", "server.properties")]
    [InlineData("config/server.properties", "config/server.properties")]
    [InlineData(@"config\server.properties", "config/server.properties")]
    public void NormalizeRelativePath_accepts_safe_relative_paths(string? input, string expected)
    {
        var actual = ServerPropertiesPathUtils.NormalizeRelativePath(input);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("../server.properties")]
    [InlineData("config/../server.properties")]
    [InlineData("/tmp/server.properties")]
    [InlineData(@"C:\temp\server.properties")]
    public void NormalizeRelativePath_rejects_paths_outside_instance(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerPropertiesPathUtils.NormalizeRelativePath(input));

        Assert.Equal(ServerPropertiesPathUtils.InvalidPathMessage, ex.Message);
    }

    [Fact]
    public void ResolveFullPath_combines_instance_base_and_normalized_relative_path()
    {
        var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(basePath);
        try
        {
            var server = new McServerInfo.ServerInfo
            {
                ID = 1,
                Name = "Test",
                Base = basePath,
                Java = "java",
                Core = "server.jar",
                ServerPropertiesPath = "config/server.properties"
            };

            var actual = ServerPropertiesPathUtils.ResolveFullPath(server);
            var expected = Path.GetFullPath(Path.Combine(basePath, "config", "server.properties"));

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }
}


public partial class ServerPropertiesPathUtilsRequestTests
{
    [Fact]
    public void UpdateServerRequest_accepts_blank_server_properties_path_as_default()
    {
        var request = CreateValidRequest();
        request.ServerPropertiesPath = "";

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.ServerPropertiesPath)));
    }

    [Theory]
    [InlineData("config/server.properties")]
    [InlineData(@"config\server.properties")]
    public void UpdateServerRequest_accepts_safe_relative_server_properties_path(string path)
    {
        var request = CreateValidRequest();
        request.ServerPropertiesPath = path;

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.ServerPropertiesPath)));
    }

    [Theory]
    [InlineData("../server.properties")]
    [InlineData("config/../server.properties")]
    [InlineData("/tmp/server.properties")]
    [InlineData(@"C:\temp\server.properties")]
    public void UpdateServerRequest_rejects_unsafe_server_properties_path(string path)
    {
        var request = CreateValidRequest();
        request.ServerPropertiesPath = path;

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.ServerPropertiesPath)) &&
            result.ErrorMessage == ServerPropertiesPathUtils.InvalidPathMessage);
    }

    [Fact]
    public void UpdateServerRequest_accepts_blank_world_path_as_default()
    {
        var request = CreateValidRequest();
        request.WorldPath = "";

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.WorldPath)));
    }

    [Theory]
    [InlineData("world")]
    [InlineData("survival_world")]
    [InlineData("saves/world")]
    [InlineData(@"saves\world")]
    public void UpdateServerRequest_accepts_safe_relative_world_path(string path)
    {
        var request = CreateValidRequest();
        request.WorldPath = path;

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.WorldPath)));
    }

    [Theory]
    [InlineData("../world")]
    [InlineData("saves/../world")]
    [InlineData("/tmp/world")]
    [InlineData(@"C:\temp\world")]
    public void UpdateServerRequest_rejects_unsafe_world_path(string path)
    {
        var request = CreateValidRequest();
        request.WorldPath = path;

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.WorldPath)) &&
            result.ErrorMessage == "地图目录路径必须是实例目录内的相对路径");
    }

    [Fact]
    public void UpdateServerRequest_accepts_blank_RegionPath_as_default()
    {
        var request = CreateValidRequest();
        request.RegionPath = "";

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.RegionPath)));
    }

    [Theory]
    [InlineData("region")]
    [InlineData("DIM-1/region")]
    [InlineData("DIM1/region")]
    [InlineData(@"dimensions\minecraft\overworld\region")]
    public void UpdateServerRequest_accepts_safe_relative_RegionPath(string path)
    {
        var request = CreateValidRequest();
        request.RegionPath = path;

        var results = Validate(request);

        Assert.DoesNotContain(results, result => result.MemberNames.Contains(nameof(request.RegionPath)));
    }

    [Theory]
    [InlineData("../region")]
    [InlineData("DIM-1/../region")]
    [InlineData("/tmp/region")]
    [InlineData(@"C:\temp\region")]
    public void UpdateServerRequest_rejects_unsafe_RegionPath(string path)
    {
        var request = CreateValidRequest();
        request.RegionPath = path;

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.RegionPath)) &&
            result.ErrorMessage == "Region 目录路径必须是地图目录内的相对路径");
    }

    private static UpdateServerRequest CreateValidRequest()
    {
        return new UpdateServerRequest
        {
            ID = 1,
            Name = "Test",
            Base = "C:/Servers/Test",
            Java = "java",
            Core = "server.jar",
            MinM = 1024,
            MaxM = 2048
        };
    }

    private static List<System.ComponentModel.DataAnnotations.ValidationResult> Validate(UpdateServerRequest request)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(request);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, context, results, true);
        return results;
    }
}
