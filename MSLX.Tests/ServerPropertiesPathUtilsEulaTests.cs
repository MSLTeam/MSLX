using MSLX.Daemon.Utils;
using MSLX.SDK.Models;

namespace MSLX.Tests;

public class ServerPropertiesPathUtilsEulaTests
{
    [Fact]
    public void ResolveEulaPath_returns_base_eula_for_default_layout()
    {
        var basePath = CreateTempDir();
        try
        {
            var server = MakeServer(basePath, "server.properties");

            var actual = ServerPropertiesPathUtils.ResolveEulaPath(server);
            var expected = Path.GetFullPath(Path.Combine(basePath, "eula.txt"));

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    [Fact]
    public void ResolveEulaPath_follows_server_properties_subdirectory_for_mcdr()
    {
        var basePath = CreateTempDir();
        try
        {
            // MCDR 布局：server.properties 位于 server/ 子目录，eula.txt 应同级
            var server = MakeServer(basePath, "server/server.properties");

            var actual = ServerPropertiesPathUtils.ResolveEulaPath(server);
            var expected = Path.GetFullPath(Path.Combine(basePath, "server", "eula.txt"));

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    [Fact]
    public void ResolveEulaPath_handles_backslash_separators()
    {
        var basePath = CreateTempDir();
        try
        {
            var server = MakeServer(basePath, @"config\server.properties");

            var actual = ServerPropertiesPathUtils.ResolveEulaPath(server);
            var expected = Path.GetFullPath(Path.Combine(basePath, "config", "eula.txt"));

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    [Fact]
    public void ResolveEulaPath_falls_back_to_base_for_unsafe_path()
    {
        var basePath = CreateTempDir();
        try
        {
            // 非法路径(目录穿越)应回退到实例根目录，绝不逃出实例目录
            var server = MakeServer(basePath, "../evil/server.properties");

            var actual = ServerPropertiesPathUtils.ResolveEulaPath(server);
            var expected = Path.Combine(basePath, "eula.txt");

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(basePath, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static McServerInfo.ServerInfo MakeServer(string basePath, string propertiesPath)
    {
        return new McServerInfo.ServerInfo
        {
            ID = 1,
            Name = "Test",
            Base = basePath,
            Java = "none",
            Core = "none",
            ServerPropertiesPath = propertiesPath
        };
    }
}
