using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.FilesControllers;
using MSLX.Daemon.Controllers.InstanceControllers;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using MSLX.SDK.Models.Instance;
using Newtonsoft.Json.Linq;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public sealed class PluginsAndModsPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mslx-pm-{Guid.NewGuid():N}");
    private readonly ServerListConfig _previousServers = IConfigBase.ServerList;
    private readonly UserListConfig _previousUsers = IConfigBase.UserList;
    private readonly Dictionary<string, string?> _files;
    private readonly McServerInfo.ServerInfo _server;
    private readonly ControllerContext _context = new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", "pm-path-test")]))
        }
    };

    public PluginsAndModsPathTests()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        _files = new[] { "ServerList.json", "UserList.json" }.ToDictionary(
            name => Path.Combine(IConfigBase.GetAppConfigPath(), name),
            name => File.Exists(Path.Combine(IConfigBase.GetAppConfigPath(), name))
                ? File.ReadAllText(Path.Combine(IConfigBase.GetAppConfigPath(), name)) : null);
        Directory.CreateDirectory(_root);
        IConfigBase.UserList = new UserListConfig();
        Assert.True(IConfigBase.UserList.CreateUser(new UserInfo
        {
            Id = "pm-path-test", Username = "pm-path-test", Role = "admin", PasswordHash = "test"
        }));
        IConfigBase.ServerList = new ServerListConfig();
        _server = new McServerInfo.ServerInfo { ID = 990290, Name = "pm-path-test", Base = _root, Java = "docker:test", Core = "server.jar" };
        SaveServer();
    }

    private void SaveServer() => IConfigBase.ServerList.WriteServerList(new JArray(JObject.FromObject(_server)));

    private JObject List(string mode = "mods", bool checkClient = false)
    {
        var controller = new PluginsAndModsController { ControllerContext = _context };
        var result = Assert.IsType<OkObjectResult>(controller.GetPluginsAndModsList(990290, mode, checkClient));
        return (JObject)JObject.FromObject(result.Value!)["Data"]!;
    }

    [Theory]
    [InlineData("mods", "mods")]
    [InlineData("mods", "server/mods")]
    [InlineData("plugins", "server/plugins")]
    [InlineData("mods", @"custom\nested\mods")]
    public void ListReturnsConfiguredPathAndDoesNotCreateMissingDirectory(string mode, string configuredPath)
    {
        _server.ModsPath = _server.PluginsPath = configuredPath;
        SaveServer();
        var data = List(mode, true);
        Assert.Equal(configuredPath.Replace('\\', '/'), (string?)data["path"]);
        Assert.False((bool)data["directoryExists"]!);
        foreach (var field in new[] { "totalCount", "activeCount", "disabledCount", "clientOnlyCount" })
            Assert.Equal(0, (int)data[field]!);
        foreach (var field in new[] { "jarFiles", "disableJarFiles", "clientJarFiles" })
            Assert.Empty(data[field]!);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void ListAndStateChangesUseTheSameConfiguredDirectory()
    {
        _server.ModsPath = "server/mods";
        SaveServer();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "server/mods")).FullName;
        File.WriteAllText(Path.Combine(directory, "test.jar"), "test");
        File.WriteAllText(Path.Combine(directory, "other.jar.disabled"), "test");
        var data = List();
        Assert.True((bool)data["directoryExists"]!);
        Assert.Equal(2, (int)data["totalCount"]!);
        var controller = new PluginsAndModsController { ControllerContext = _context };
        foreach (var action in new[] { "disable", "enable", "delete" })
        {
            var filename = action == "enable" ? "test.jar.disabled" : "test.jar";
            Assert.IsType<OkObjectResult>(controller.SetPluginOrModState(990290,
                new SetPluginModStateRequest { Mode = "mods", Action = action, Targets = [filename] }));
        }
        Assert.False(File.Exists(Path.Combine(directory, "test.jar")));
        Assert.False(Directory.Exists(Path.Combine(_root, "mods")));
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("/tmp/mods")]
    [InlineData(@"C:\temp\mods")]
    public void ListStillRejectsUnsafeConfiguredPaths(string path)
    {
        _server.ModsPath = path;
        SaveServer();
        var controller = new PluginsAndModsController { ControllerContext = _context };
        Assert.IsType<BadRequestObjectResult>(controller.GetPluginsAndModsList(990290, "mods"));
    }

    [Theory]
    [InlineData("server/mods", "server/plugins", "server/world")]
    [InlineData("custom/mods", "custom/plugins", "custom/world")]
    [InlineData("mods", "plugins", "world")]
    public async Task ExportExcludesConfiguredDirectoriesWithoutExcludingUnrelatedFolders(string mods, string plugins, string world)
    {
        _server.ModsPath = mods;
        _server.PluginsPath = plugins;
        _server.WorldPath = world;
        SaveServer();
        foreach (var folder in new[] { mods, plugins, world, mods + "-backup", "plugins" }.Distinct())
        {
            Directory.CreateDirectory(Path.Combine(_root, folder));
            File.WriteAllText(Path.Combine(_root, folder, "test.txt"), "test");
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var manager = new BackgroundTaskManager(NullLogger<BackgroundTaskManager>.Instance, new CreationTaskTracker());
        var controller = new InstanceExportController(cache, null!, manager) { ControllerContext = _context };
        Assert.IsType<OkObjectResult>(controller.ExportInstance(990290, new ExportInstanceRequest { Excludes = ["mods", "plugins", "world"] }));
        var task = Assert.Single(manager.GetUserTasks("pm-path-test"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (task.State is TaskState.Pending or TaskState.Running)
            await Task.Delay(20, timeout.Token);
        Assert.Equal(TaskState.Success, task.State);
        // Task success is set just before the archive is disposed; wait for the background writer to close it.
        ZipArchive? zip = null;
        while (zip == null)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try { zip = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(Path.Combine(_root, "mslx-packs"), "*.zip"))); }
            catch (IOException) { await Task.Delay(20, timeout.Token); }
        }
        using (zip)
        {
            var entries = zip.Entries.Select(entry => entry.FullName).ToArray();
            foreach (var excluded in new[] { mods, plugins, world })
                Assert.DoesNotContain(entries, entry => entry.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(mods + "-backup/test.txt", entries);
            if (plugins != "plugins") Assert.Contains("plugins/test.txt", entries);
        }
    }

    public void Dispose()
    {
        IConfigBase.ServerList.Dispose();
        IConfigBase.UserList.Dispose();
        IConfigBase.ServerList = _previousServers;
        IConfigBase.UserList = _previousUsers;
        foreach (var (path, content) in _files)
        {
            if (content == null) File.Delete(path);
            else File.WriteAllText(path, content);
        }
        Directory.Delete(_root, true);
    }
}
