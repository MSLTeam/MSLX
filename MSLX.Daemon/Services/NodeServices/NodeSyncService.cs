using System.Net;
using System.Security.Claims;
using System.Text;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using MSLX.SDK.Models.Node;
using MSLX.SDK.Models.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services.NodeServices;

public sealed class NodeSyncService(PluginManager plugins, PluginPackageService packages, BackgroundTaskManager tasks, ILogger<NodeSyncService> logger)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private sealed record SourcePlugin(PluginManifest Manifest, JObject? Config);
    private sealed record Source(JObject Settings, List<SourcePlugin> Plugins, string Revision);
    private sealed record Target(JObject Settings, Dictionary<string, PluginManifest?> Plugins, string Revision);

    private static UserInfo CheckCaller(ClaimsPrincipal caller)
    {
        var user = NodeCallerAuthorization.GetCaller(caller);
        if (user == null || user.Role != "admin")
            throw new HttpRequestException("管理员会话或 APIKey 已失效，同步已停止", null, HttpStatusCode.Unauthorized);
        return user;
    }

    private static SlaveNodeConfig GetTarget(string id)
    {
        var node = IConfigBase.NodeList.GetNodeById(id);
        return node != null && !node.PendingRemoval ? node : throw new SyncConflictException("节点不存在或已解除链接: " + id);
    }

    private static bool AuthorizationLost(Exception exception, string id) =>
        exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } ||
        IConfigBase.NodeList.GetNodeById(id) is null or { PendingRemoval: true };

    private async Task<JToken> Call(string id, ClaimsPrincipal caller, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var user = CheckCaller(caller);
        var node = GetTarget(id);
        using var request = new HttpRequestMessage(method, node.NodeUrl.TrimEnd('/') + path) { Content = content };
        request.Headers.Add("x-node-id", node.NodeId);
        request.Headers.Add("x-user-token", JwtUtils.GenerateNodeToken(user, node, caller));
        using var response = await Client.SendAsync(request, ct);
        JObject body;
        try { body = JObject.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new HttpRequestException($"节点返回的接口数据无效（HTTP {(int)response.StatusCode}）"); }
        if (!response.IsSuccessStatusCode || body["code"]?.Value<int>() != 200)
            throw new HttpRequestException(body["message"]?.ToString() ?? $"节点请求失败（HTTP {(int)response.StatusCode}）", null, response.StatusCode);
        return body["data"] ?? JValue.CreateNull();
    }

    private static StringContent Json(object value) => new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");

    private async Task<Source> ReadSource(NodeSyncRequest request, CancellationToken ct)
    {
        if ((bool?)IConfigBase.Config.ReadConfigKey("IsSlaveMode") == true) throw new InvalidDataException("同步只能从主节点发起");
        request.TargetNodeIds = request.TargetNodeIds.Distinct(StringComparer.Ordinal).ToList();
        request.PluginIds = request.PluginIds.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        request.SettingKeys = request.SettingKeys.Distinct().Order(StringComparer.Ordinal).ToList();
        if (request.TargetNodeIds.Count is < 1 or > 100 || request.PluginIds.Count > 100 || request.SettingKeys.Count + request.PluginIds.Count == 0)
            throw new InvalidDataException("请选择目标子节点及同步内容（最多 100 个节点、100 个插件）");
        foreach (var id in request.TargetNodeIds) GetTarget(id);
        var settings = NodeSettingsSync.Select(IConfigBase.Config.ReadConfig(), request.SettingKeys);
        var items = new List<SourcePlugin>();
        foreach (var id in request.PluginIds)
        {
            PluginPackageService.ValidateId(id);
            var gate = plugins.GetOperationLock(id);
            await gate.WaitAsync(ct);
            try
            {
                var manifest = packages.Describe(id) ?? throw new InvalidDataException("主节点未安装插件: " + id);
                if (manifest.Pending) throw new InvalidDataException("源插件有待处理操作或未成功加载: " + id);
                var config = request.IncludePluginConfig ? PluginPackageService.ReadConfig(manifest.Id) : null;
                if (request.IncludePluginConfig) manifest.ConfigHash = config == null ? null : PluginPackageService.ConfigHash(config);
                items.Add(new(manifest, config));
            }
            finally { gate.Release(); }
        }
        var revision = PluginPackageService.Hash(new JObject
        {
            ["settings"] = settings,
            ["plugins"] = new JArray(items.Select(p => new JObject { ["id"] = p.Manifest.Id, ["revision"] = p.Manifest.Revision })),
            ["includeConfig"] = request.IncludePluginConfig
        }.ToString(Formatting.None));
        return new(settings, items, revision);
    }

    private async Task<Target> ReadTarget(string id, NodeSyncRequest request, ClaimsPrincipal caller, CancellationToken ct, Source source)
    {
        var protocol = await Call(id, caller, HttpMethod.Get, "/api/node/protocol", null, ct);
        if (protocol["features"]?["nodeSync"]?.Value<bool>() != true)
            throw new InvalidDataException("子节点不支持设置和插件同步，请一起升级主、子节点");
        if (source.Plugins.Count > 0)
        {
            var status = await Call(id, caller, HttpMethod.Get, "/api/status", null, ct);
            var formatted = System.Text.RegularExpressions.Regex.Match(status["version"]?.ToString() ?? "", @"\d+(?:\.\d+){1,3}").Value;
            if (!Version.TryParse(formatted, out var targetVersion)) throw new InvalidDataException("无法识别子节点版本，请升级节点后重试");
            foreach (var item in source.Plugins)
                if (!Version.TryParse(item.Manifest.MinSdkVersion.TrimStart('v', 'V'), out var minimum) || minimum > targetVersion)
                    throw new InvalidDataException($"插件 {item.Manifest.Id} 要求节点版本 {item.Manifest.MinSdkVersion}，目标节点 {targetVersion} 不兼容");
        }
        var settings = request.SettingKeys.Count == 0 ? new JObject() : NodeSettingsSync.Select((JObject)await Call(id, caller, HttpMethod.Get, "/api/settings", null, ct), request.SettingKeys);
        var installed = request.PluginIds.Count == 0 ? new JArray() : (JArray)await Call(id, caller, HttpMethod.Get, "/api/plugins/list?includeSyncInfo=true", null, ct);
        var targetPlugins = new Dictionary<string, PluginManifest?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pluginId in request.PluginIds)
        {
            var info = installed.FirstOrDefault(p => string.Equals(p["id"]?.ToString(), pluginId, StringComparison.OrdinalIgnoreCase));
            var manifest = info?["syncInfo"]?.Type == JTokenType.Object ? info["syncInfo"]!.ToObject<PluginManifest>() : null;
            if (info != null && manifest == null) throw new InvalidDataException("目标插件尚未稳定安装，不能同步: " + pluginId);
            if (manifest?.Pending == true) throw new InvalidDataException("目标插件有待处理操作: " + pluginId);
            targetPlugins[pluginId] = manifest;
        }
        var revision = PluginPackageService.Hash(new JObject
        {
            ["settings"] = settings,
            ["plugins"] = new JObject(request.PluginIds.Select(id => new JProperty(id, targetPlugins[id]?.Revision ?? "missing")))
        }.ToString(Formatting.None));
        return new(settings, targetPlugins, revision);
    }

    public async Task<JObject> PreviewAsync(NodeSyncRequest request, ClaimsPrincipal caller, CancellationToken ct)
    {
        CheckCaller(caller);
        var source = await ReadSource(request, ct);
        var targets = new JArray();
        foreach (var id in request.TargetNodeIds)
        {
            var target = await ReadTarget(id, request, caller, ct, source);
            targets.Add(new JObject
            {
                ["nodeId"] = id, ["nodeName"] = GetTarget(id).NodeName, ["revision"] = target.Revision,
                ["settings"] = new JArray(source.Settings.Properties().Select(p => new JObject
                {
                    ["key"] = p.Name, ["source"] = p.Value.DeepClone(), ["target"] = target.Settings[p.Name]?.DeepClone(),
                    ["changed"] = !JToken.DeepEquals(p.Value, target.Settings[p.Name])
                })),
                ["plugins"] = new JArray(source.Plugins.Select(p => new JObject
                {
                    ["id"] = p.Manifest.Id, ["sourceVersion"] = p.Manifest.Version, ["targetVersion"] = target.Plugins[p.Manifest.Id]?.Version,
                    ["sourceEnabled"] = p.Manifest.Enabled, ["targetEnabled"] = target.Plugins[p.Manifest.Id]?.Enabled,
                    ["programChanged"] = p.Manifest.PackageHash != target.Plugins[p.Manifest.Id]?.PackageHash,
                    ["configAvailable"] = p.Config != null,
                    ["configChanged"] = request.IncludePluginConfig && p.Config != null && p.Manifest.ConfigHash != target.Plugins[p.Manifest.Id]?.ConfigHash
                }))
            });
        }
        return new JObject { ["sourceRevision"] = source.Revision, ["targets"] = targets };
    }

    public async Task<string> StartAsync(NodeSyncRequest request, ClaimsPrincipal caller, CancellationToken ct)
    {
        var user = CheckCaller(caller);
        var preview = await PreviewAsync(request, caller, ct);
        if (preview["sourceRevision"]?.ToString() != request.SourceRevision || ((JArray)preview["targets"]!).Any(t =>
            !request.TargetRevisions.TryGetValue(t["nodeId"]!.ToString(), out var expected) || expected != t["revision"]!.ToString()))
            throw new SyncConflictException("同步来源或目标已变化，请重新预览");
        var source = await ReadSource(request, ct);
        if (source.Revision != request.SourceRevision) throw new SyncConflictException("主节点内容已变化，请重新预览");
        var staging = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "NodeSync", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var item in source.Plugins)
            {
                var gate = plugins.GetOperationLock(item.Manifest.Id);
                await gate.WaitAsync(ct);
                try
                {
                    var path = Path.Combine(staging, item.Manifest.Id + ".dll");
                    File.Copy(item.Manifest.DllPath, path);
                    if (PluginPackageService.FileHash(path) != item.Manifest.PackageHash) throw new SyncConflictException("源插件已变化，请重新预览");
                    item.Manifest.DllPath = path;
                }
                finally { gate.Release(); }
            }
            var (task, token) = tasks.CreateTask(user.Id, 0, TaskType.NodeSync, "从主节点同步", $"{request.TargetNodeIds.Count} 个子节点");
            task.SyncResults = request.TargetNodeIds.SelectMany(id => (request.SettingKeys.Count > 0 ? new[] { "settings" } : Array.Empty<string>())
                .Concat(request.PluginIds).Select(item => new NodeSyncItemResult { NodeId = id, Item = item })).ToList();
            var principal = new ClaimsPrincipal(caller.Identities.Select(identity => identity.Clone()));
            _ = Task.Run(() => RunAsync(task, request, source, principal, staging, token), CancellationToken.None);
            return task.Id;
        }
        catch { Directory.Delete(staging, true); throw; }
    }

    private async Task RunAsync(BackgroundTaskItem task, NodeSyncRequest request, Source source, ClaimsPrincipal caller, string staging, CancellationToken ct)
    {
        try
        {
            foreach (var id in request.TargetNodeIds)
            {
                ct.ThrowIfCancellationRequested();
                CheckCaller(caller);
                Target target;
                try
                {
                    target = await ReadTarget(id, request, caller, ct, source);
                    if (target.Revision != request.TargetRevisions[id]) throw new SyncConflictException("目标已变化，请重新预览后再同步");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && !AuthorizationLost(ex, id))
                {
                    foreach (var row in task.SyncResults!.Where(r => r.NodeId == id)) { row.Status = "failed"; row.Message = ex.Message; }
                    continue;
                }
                foreach (var row in task.SyncResults!.Where(r => r.NodeId == id))
                {
                    ct.ThrowIfCancellationRequested();
                    CheckCaller(caller);
                    row.Status = "running";
                    try
                    {
                        if (row.Item == "settings")
                        {
                            if (!JToken.DeepEquals(source.Settings, target.Settings))
                                await Call(id, caller, HttpMethod.Post, "/api/settings/sync", Json(new SettingsSyncRequest
                                { Values = source.Settings, ExpectedRevision = NodeSettingsSync.Revision(target.Settings) }), CancellationToken.None);
                        }
                        else
                        {
                            var item = source.Plugins.Single(p => p.Manifest.Id.Equals(row.Item, StringComparison.OrdinalIgnoreCase));
                            var existing = target.Plugins[row.Item];
                            if (existing == null || existing.PackageHash != item.Manifest.PackageHash || existing.Enabled != item.Manifest.Enabled ||
                                item.Config != null && existing.ConfigHash != item.Manifest.ConfigHash)
                                await UploadAsync(id, caller, item, existing?.Revision ?? "missing", ct);
                        }
                        row.Status = "success";
                        row.Message = row.Item != "settings" && request.IncludePluginConfig && source.Plugins.Single(p => p.Manifest.Id.Equals(row.Item, StringComparison.OrdinalIgnoreCase)).Config == null
                            ? "程序和状态已同步；源配置不存在，已跳过配置" : "已同步";
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested) && !AuthorizationLost(ex, id))
                    { row.Status = "failed"; row.Message = ex.Message; }
                    var done = task.SyncResults.Count(r => r.Status is "success" or "failed");
                    if (!ct.IsCancellationRequested) tasks.UpdateProgress(task.Id, done * 100 / task.SyncResults.Count, $"{done}/{task.SyncResults.Count} 项已处理");
                }
            }
            ct.ThrowIfCancellationRequested();
            var failed = task.SyncResults!.Count(r => r.Status == "failed");
            if (failed == 0) tasks.SetSuccess(task.Id, "所选设置和插件已同步");
            else tasks.SetFailed(task.Id, $"同步完成，{failed} 项失败；已成功项目保留，可查看明细后重试");
        }
        catch (Exception ex)
        {
            foreach (var row in task.SyncResults!.Where(r => r.Status is "pending" or "running")) { row.Status = "skipped"; row.Message = ct.IsCancellationRequested ? "任务已取消" : ex.Message; }
            if (!ct.IsCancellationRequested) tasks.SetFailed(task.Id, ex.Message);
        }
        finally
        {
            try { Directory.Delete(staging, true); }
            catch (IOException ex) { logger.LogWarning(ex, "清理同步临时包失败"); }
        }
    }

    private async Task UploadAsync(string id, ClaimsPrincipal caller, SourcePlugin item, string expectedRevision, CancellationToken ct)
    {
        var upload = await Call(id, caller, HttpMethod.Post, "/api/files/upload/init", Json(new { }), ct);
        var uploadId = upload["uploadId"]!.ToString();
        try
        {
            await using var stream = File.OpenRead(item.Manifest.DllPath);
            var buffer = new byte[1024 * 1024];
            var index = 0;
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(index.ToString()), "index");
                form.Add(new ByteArrayContent(buffer, 0, count), "file", item.Manifest.Id + ".dll");
                await Call(id, caller, HttpMethod.Post, "/api/files/upload/chunk/" + uploadId, form, ct);
                index++;
            }
            await Call(id, caller, HttpMethod.Post, "/api/files/upload/finish/" + uploadId, Json(new { totalChunks = index }), ct);
            // 完成进行中的单个任务才做取消
            await Call(id, caller, HttpMethod.Post, "/api/plugins/upload", Json(new UploadPluginRequest
            {
                FileId = uploadId, FileName = item.Manifest.Id + ".dll", ExpectedPluginId = item.Manifest.Id,
                PackageHash = item.Manifest.PackageHash, ExpectedRevision = expectedRevision, Enabled = item.Manifest.Enabled, Config = item.Config
            }), CancellationToken.None);
        }
        finally
        {
            try { await Call(id, caller, HttpMethod.Post, "/api/files/upload/delete/" + uploadId, Json(new { }), CancellationToken.None); }
            catch (Exception ex) { logger.LogDebug("同步上传暂存清理未确认: {Reason}", ex.GetType().Name); }
        }
    }
}
