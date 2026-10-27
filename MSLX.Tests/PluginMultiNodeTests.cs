using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK;
using MSLX.SDK.Models.Node;
using MSLX.SDK.Models.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Utils;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public class PluginMultiNodeTests
{
    [Theory]
    [InlineData(PluginMultiNodeSupport.NoTest, "\"no-test\"")]
    [InlineData(PluginMultiNodeSupport.Supported, "\"true\"")]
    [InlineData(PluginMultiNodeSupport.Unsupported, "\"false\"")]
    public void Declaration_UsesOnlyStringEnumValues(PluginMultiNodeSupport value, string json)
    {
        Assert.Equal(json, JsonConvert.SerializeObject(value));
        Assert.Equal(value, JsonConvert.DeserializeObject<PluginMultiNodeSupport>(json));
    }

    [Theory]
    [InlineData("true")][InlineData("false")][InlineData("0")][InlineData("null")][InlineData("\"unknown\"")]
    public void Declaration_RejectsBooleansNumbersNullAndUnknownStrings(string json) =>
        Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<PluginMultiNodeSupport>(json));

    [Fact]
    public void LegacyPlugin_DefaultsToNoTest_AndInvalidSdkDeclarationIsRejected()
    {
        var manager = new PluginManager();
        var metadata = manager.GetPluginMetadata(typeof(global::MSLX.NodeTestPlugin.TestPlugin).Assembly.Location, true)!;
        Assert.Equal(PluginMultiNodeSupport.NoTest, metadata.SupportsMultiNode);
        Assert.Throws<InvalidDataException>(() => PluginManager.ValidateMetadata(new InvalidDeclaration()));
        Assert.Throws<JsonSerializationException>(() => JsonConvert.SerializeObject((PluginMultiNodeSupport)99));
    }

    private sealed class InvalidDeclaration : IPlugin
    {
        public string Id => "invalid-declaration";
        public PluginMultiNodeSupport SupportsMultiNode => (PluginMultiNodeSupport)99;
    }

    private sealed class FuturePlugin : IPlugin
    {
        public string Id => "future-plugin";
        public string MinSDKVersion => "999.0.0";
    }

    [Fact]
    public void Installation_RejectsIncompatibleSdkBeforeChangingFiles() =>
        Assert.Throws<InvalidDataException>(() => PluginPackageService.ValidateCompatibility(new FuturePlugin()));

    [Fact]
    public void ReadingSslSettings_DoesNotGenerateOrReplaceCertificates()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var subject = SslCertificateManager.CurrentSubject;
        var certDir = Path.Combine(IConfigBase.GetAppConfigPath(), "certs");
        var files = Directory.Exists(certDir) ? Directory.GetFiles(certDir).Order().ToArray() : [];
        Assert.IsType<OkObjectResult>(new SettingsController().GetSslSettings());
        Assert.Equal(subject, SslCertificateManager.CurrentSubject);
        Assert.Equal(files, Directory.Exists(certDir) ? Directory.GetFiles(certDir).Order().ToArray() : []);
    }

    [Theory]
    [InlineData("admin", "/api/settings", true)]
    [InlineData("admin", "/api/settings/ssl", true)]
    [InlineData("admin", "/api/settings/sync", true)]
    [InlineData("admin", "/api/settings/webpanel/style", false)]
    [InlineData("admin", "/api/admin/user/create", false)]
    [InlineData("user", "/api/settings", false)]
    [InlineData("user", "/api/settings/sync", false)]
    public async Task DelegatedSettings_OnlyAllowAdminNodeSettings(string role, string path, bool allowed)
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var original = IConfigBase.Config.ReadConfig();
        var masters = IConfigBase.MasterNodes;
        var users = IConfigBase.UserList;
        using var temporaryUsers = new UserListConfig();
        var masterPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        var file = File.Exists(masterPath) ? File.ReadAllText(masterPath) : null;
        using var temporaryMasters = new MasterNodesConfig();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        try
        {
            IConfigBase.Config.WriteConfigKey("IsSlaveMode", true);
            IConfigBase.UserList = temporaryUsers;
            IConfigBase.MasterNodes = temporaryMasters;
            temporaryMasters.AddOrUpdateMaster(new MasterNodeInfo { NodeId = "plugin-settings-test", MasterId = "plugin-settings-master", CommsKey = "test" });
            const string token = "test-node-credential";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("UserId", "node:plugin-settings-test:user"), new Claim(ClaimTypes.Role, role) }, "NodeAuth"));
            var until = DateTimeOffset.UtcNow.AddMinutes(1);
            cache.Set("NODE_AUTH_plugin-settings-test_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                new NodeAuthorization.Lease(principal, [], until), until);
            var reached = false;
            var middleware = new AuthMiddleware(_ => { reached = true; return Task.CompletedTask; }, cache);
            var http = new DefaultHttpContext();
            http.Request.Path = path;
            http.Request.Method = "POST";
            http.Request.Headers["x-user-token"] = token;
            http.Request.Headers["x-node-id"] = "plugin-settings-test";
            http.Response.Body = new MemoryStream();
            await middleware.InvokeAsync(http, new NodeAuthorization(cache));
            Assert.Equal(allowed, reached);
            Assert.Equal(allowed ? 200 : 403, http.Response.StatusCode);
        }
        finally
        {
            IConfigBase.Config.WriteConfig(original);
            IConfigBase.MasterNodes = masters;
            IConfigBase.UserList = users;
            if (file == null) File.Delete(masterPath); else File.WriteAllText(masterPath, file);
        }
    }

    [Theory]
    [InlineData("listenPort")][InlineData("jwtSecret")][InlineData("enableSsl")][InlineData("apiKey")][InlineData("oAuthMSLClientSecret")]
    public void SettingsSync_RejectsSensitiveAndNetworkFields(string key) =>
        Assert.Throws<InvalidDataException>(() => NodeSettingsSync.Validate(new JObject { [key] = true }));

    [Fact]
    public void SettingsSync_IsAtomicAndRejectsStalePreview_WithoutChangingOtherSettings()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var original = IConfigBase.Config.ReadConfig();
        try
        {
            var controller = new SettingsController();
            var before = NodeSettingsSync.Select(original, ["downloadThreadCount"]);
            var next = before["downloadThreadCount"]!.Value<int>() == 7 ? 6 : 7;
            var request = new SettingsSyncRequest { Values = new JObject { ["downloadThreadCount"] = next }, ExpectedRevision = NodeSettingsSync.Revision(before) };
            Assert.IsType<OkObjectResult>(controller.SyncSettings(request));
            var after = IConfigBase.Config.ReadConfig();
            Assert.Equal(next, after["downloadThreadCount"]!.Value<int>());
            after.Remove("downloadThreadCount");
            var untouched = (JObject)original.DeepClone(); untouched.Remove("downloadThreadCount");
            Assert.True(JToken.DeepEquals(untouched, after));
            request.Values["downloadThreadCount"] = next == 7 ? 6 : 7;
            Assert.IsType<ConflictObjectResult>(controller.SyncSettings(request));
            Assert.Equal(next, IConfigBase.Config.ReadConfigKey("downloadThreadCount")!.Value<int>());
            request.Values["apiKey"] = "should-not-write";
            Assert.IsType<BadRequestObjectResult>(controller.SyncSettings(request));
            Assert.Equal(original["apiKey"], IConfigBase.Config.ReadConfigKey("apiKey"));
        }
        finally { IConfigBase.Config.WriteConfig(original); }
    }

    [Fact]
    public async Task PluginInstall_LoadFailureRestoresProgramConfigurationAndDisabledState()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var manager = new PluginManager(); // No running host: loading deliberately fails.
        var packages = new PluginPackageService(manager, NullLogger<PluginPackageService>.Instance);
        var id = "mslx-plugin-node-fixture";
        var root = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
        Directory.CreateDirectory(root);
        var dll = Path.Combine(root, id + ".dll");
        var configDir = Path.GetDirectoryName(PluginPackageService.ConfigPath(id))!;
        Assert.False(File.Exists(dll), "Fixture must not overwrite an existing test plugin");
        var candidate = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        var hadConfig = File.Exists(PluginPackageService.ConfigPath(id));
        var priorConfig = hadConfig ? PluginPackageService.ReadConfig(id) : null;
        try
        {
            File.Copy(typeof(global::MSLX.NodeTestPlugin.TestPlugin).Assembly.Location, dll);
            await using (var stream = new FileStream(dll, FileMode.Append)) await stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            File.WriteAllText(dll + ".disabled", "");
            IConfigBase.GetPluginConfigService(id).WriteConfig(new JObject { ["marker"] = "old-secret" });
            var oldHash = PluginPackageService.FileHash(dll);
            var manifest = packages.Describe(id)!;
            File.Copy(typeof(global::MSLX.NodeTestPlugin.TestPlugin).Assembly.Location, candidate);
            await Assert.ThrowsAsync<IOException>(() => packages.InstallAsync(candidate, new UploadPluginRequest
            {
                ExpectedPluginId = id, Enabled = true, ExpectedRevision = manifest.Revision,
                Config = new JObject { ["marker"] = "new-secret" }
            }, TestContext.Current.CancellationToken));
            Assert.Equal(oldHash, PluginPackageService.FileHash(dll));
            Assert.True(File.Exists(dll + ".disabled"));
            Assert.Equal("old-secret", IConfigBase.GetPluginConfigService(id).ReadConfigKey("marker")!.Value<string>());
            // Same installer can update a disabled plugin without launching it, preserving config when not selected.
            var result = await packages.InstallAsync(candidate, new UploadPluginRequest { Enabled = false, ExpectedRevision = packages.Describe(id)!.Revision }, TestContext.Current.CancellationToken);
            Assert.False(result.Enabled);
            Assert.Equal("old-secret", IConfigBase.GetPluginConfigService(id).ReadConfigKey("marker")!.Value<string>());
            await Assert.ThrowsAsync<SyncConflictException>(() => packages.InstallAsync(candidate, new UploadPluginRequest { ExpectedRevision = "outdated" }, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(candidate)) File.Delete(candidate);
            if (File.Exists(dll)) File.Delete(dll);
            if (File.Exists(dll + ".disabled")) File.Delete(dll + ".disabled");
            if (priorConfig != null) IConfigBase.GetPluginConfigService(id).WriteConfig(priorConfig);
            else if (!hadConfig && Directory.Exists(configDir)) Directory.Delete(configDir, true);
        }
    }
}
