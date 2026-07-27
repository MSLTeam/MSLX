using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Docker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

/// <summary>
/// 本地 Docker 镜像管理服务
/// 通过 docker CLI 实现
/// </summary>
public class DockerService : IDockerService
{
    private const string StatusCacheKey = "Docker_Env_Status";
    private const string PullTaskKeyPrefix = "Docker_Pull_Task_";
    private const int MaxLogLines = 300;

    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TaskRetention = TimeSpan.FromMinutes(30);

    // 逐层进度行：<layerId>: <status> [====>   ]  1.2MB/10MB
    private static readonly Regex LayerLinePattern =
        new(@"^([a-f0-9]{6,}):\s*(.+)$", RegexOptions.Compiled);

    private readonly ILogger<DockerService> _logger;
    private readonly IMemoryCache _cache;

    /// <summary>正在执行的拉取任务（镜像 -> 任务ID），用于合并重复提交</summary>
    private readonly ConcurrentDictionary<string, string> _runningPulls = new();

    public DockerService(ILogger<DockerService> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    #region 环境探测

    public async Task<DockerEnvStatus> GetStatusAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _cache.TryGetValue(StatusCacheKey, out DockerEnvStatus? cached) && cached != null)
        {
            return cached;
        }

        var status = new DockerEnvStatus
        {
            InContainer = IsRunningInContainer(),
            SockMounted = File.Exists("/var/run/docker.sock")
        };

        try
        {
            var result = await RunDockerAsync(["version", "--format", "{{json .}}"], TimeSpan.FromSeconds(15));

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                try
                {
                    var json = JObject.Parse(result.StandardOutput.Trim());
                    status.ClientVersion = json["Client"]?["Version"]?.ToString();
                    status.ServerVersion = json["Server"]?["Version"]?.ToString();
                    status.OsType = json["Server"]?["Os"]?.ToString() ?? json["Client"]?["Os"]?.ToString();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("[Docker-Image] docker version 输出解析失败: {Message}", ex.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(status.ServerVersion))
            {
                status.Available = true;
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;

                status.ErrorMessage = error.Trim();
                status.ErrorType = ClassifyDaemonError(status, error);
            }
        }
        catch (Exception ex)
        {
            status.Available = false;
            status.ErrorMessage = ex.Message;
            status.ErrorType = IsDockerMissing(ex) ? "notInstalled" : "unknown";
            _logger.LogWarning("[Docker-Image] Docker 环境探测失败: {Message}", ex.Message);
        }

        _cache.Set(StatusCacheKey, status, StatusCacheDuration);
        return status;
    }

    /// <summary>
    /// 归类守护进程不可用的原因
    /// </summary>
    private static string ClassifyDaemonError(DockerEnvStatus status, string error)
    {
        var lower = error.ToLowerInvariant();

        if (status.InContainer && !status.SockMounted) return "sockNotMounted";
        if (lower.Contains("permission denied")) return "permissionDenied";
        if (lower.Contains("not found") || lower.Contains("command not found") ||
            lower.Contains("不是内部或外部命令")) return "notInstalled";
        if (lower.Contains("cannot connect") || lower.Contains("daemon") ||
            lower.Contains("拒绝") || lower.Contains("refused")) return "daemonUnreachable";

        return "unknown";
    }

    private static bool IsDockerMissing(Exception ex)
    {
        return ex is System.ComponentModel.Win32Exception ||
               ex.Message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunningInContainer()
    {
        var inContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return inContainer != null && inContainer.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 镜像列表

    public async Task<List<DockerImageInfo>> ListImagesAsync(bool includeDangling = true)
    {
        var result = await RunDockerAsync(["images", "--no-trunc", "--format", "{{json .}}"],
            TimeSpan.FromSeconds(30));

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildErrorMessage(result, "获取镜像列表失败"));
        }

        var images = ParseImageList(result.StandardOutput);

        if (!includeDangling)
        {
            images = images.Where(i => !i.IsDangling).ToList();
        }

        AttachInstanceUsage(images);

        return images
            .OrderByDescending(i => i.UsedBy.Count > 0)
            .ThenByDescending(i => i.SizeBytes ?? 0)
            .ToList();
    }

    /// <summary>
    /// 解析 docker images 的 JSON 行输出
    /// </summary>
    public static List<DockerImageInfo> ParseImageList(string? stdout)
    {
        var list = new List<DockerImageInfo>();
        if (string.IsNullOrWhiteSpace(stdout)) return list;

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !trimmed.StartsWith('{')) continue;

            JObject json;
            try
            {
                json = JObject.Parse(trimmed);
            }
            catch (JsonException)
            {
                continue;
            }

            var repository = json["Repository"]?.ToString() ?? "";
            var tag = json["Tag"]?.ToString() ?? "";
            var imageId = json["ID"]?.ToString() ?? "";
            var digest = json["Digest"]?.ToString();
            var size = json["Size"]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(imageId)) continue;

            var isDangling = repository is "<none>" or "" || tag is "<none>" or "";
            var shortId = imageId.Replace("sha256:", "").PadRight(12)[..12].Trim();

            list.Add(new DockerImageInfo
            {
                Repository = repository,
                Tag = tag,
                Reference = isDangling ? imageId : $"{repository}:{tag}",
                ImageId = imageId,
                ShortId = shortId,
                Digest = string.IsNullOrWhiteSpace(digest) || digest == "<none>" ? null : digest,
                Size = size,
                SizeBytes = DockerImageResolver.ParseSizeToBytes(size),
                CreatedAt = json["CreatedAt"]?.ToString() ?? "",
                IsDangling = isDangling,
                IsMslxRuntime = DockerImageResolver.IsMslxRuntime(repository)
            });
        }

        return list;
    }

    /// <summary>
    /// 标记每个镜像被哪些实例引用
    /// </summary>
    private void AttachInstanceUsage(List<DockerImageInfo> images)
    {
        List<MSLX.SDK.Models.McServerInfo.ServerInfo> servers;
        try
        {
            servers = IConfigBase.ServerList.GetServerList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Docker-Image] 读取实例列表失败，跳过引用标记: {Message}", ex.Message);
            return;
        }

        foreach (var server in servers)
        {
            if (server.Java is not ("docker-java" or "docker-custom")) continue;
            if (string.IsNullOrWhiteSpace(server.DockerImage)) continue;

            var configured = server.DockerImage.Trim();
            var resolved = DockerImageResolver.NormalizeReference(DockerImageResolver.Resolve(configured));

            foreach (var image in images)
            {
                if (!IsSameImage(image, resolved)) continue;

                image.UsedBy.Add(new DockerImageUsage
                {
                    InstanceId = server.ID,
                    InstanceName = server.Name,
                    ConfiguredImage = configured
                });
            }
        }
    }

    private static bool IsSameImage(DockerImageInfo image, string resolvedReference)
    {
        if (string.IsNullOrWhiteSpace(resolvedReference)) return false;

        if (!image.IsDangling &&
            string.Equals(DockerImageResolver.NormalizeReference(image.Reference), resolvedReference,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 实例里也可能直接填的是镜像 ID（虽然应该很少）
        var bareId = image.ImageId.Replace("sha256:", "");
        var bareRef = resolvedReference.Replace("sha256:", "");
        return DockerImageResolver.LooksLikeImageId(bareRef) &&
               bareId.StartsWith(bareRef, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<DockerPresetImage>> ListPresetImagesAsync()
    {
        var presets = DockerImageResolver.GetPresetImages();

        List<DockerImageInfo> local;
        try
        {
            local = await ListImagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Docker-Image] 预设镜像状态检查失败: {Message}", ex.Message);
            return presets;
        }

        foreach (var preset in presets)
        {
            var match = local.FirstOrDefault(i =>
                string.Equals(DockerImageResolver.NormalizeReference(i.Reference), preset.Image,
                    StringComparison.OrdinalIgnoreCase));

            preset.Exists = match != null;
            preset.Size = match?.Size;
        }

        return presets;
    }

    #endregion

    #region 详情

    public async Task<DockerImageDetail?> InspectImageAsync(string reference)
    {
        EnsureValidReference(reference);

        var result = await RunDockerAsync(["image", "inspect", reference.Trim()], TimeSpan.FromSeconds(20));
        if (result.ExitCode != 0) return null;

        var array = JArray.Parse(result.StandardOutput);
        if (array.Count == 0) return null;

        var item = (JObject)array[0]!;
        var config = item["Config"] as JObject;

        var detail = new DockerImageDetail
        {
            ImageId = item["Id"]?.ToString() ?? "",
            RepoTags = item["RepoTags"]?.Select(t => t.ToString()).ToList() ?? [],
            RepoDigests = item["RepoDigests"]?.Select(t => t.ToString()).ToList() ?? [],
            Created = item["Created"]?.ToString(),
            Architecture = item["Architecture"]?.ToString(),
            Os = item["Os"]?.ToString(),
            Size = item["Size"]?.Value<long>() ?? 0,
            WorkingDir = config?["WorkingDir"]?.ToString(),
            Env = config?["Env"]?.Select(t => t.ToString()).ToList() ?? [],
            Entrypoint = config?["Entrypoint"]?.Select(t => t.ToString()).ToList() ?? [],
            Cmd = config?["Cmd"]?.Select(t => t.ToString()).ToList() ?? [],
            ExposedPorts = (config?["ExposedPorts"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? [],
            Volumes = (config?["Volumes"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? [],
            Layers = item["RootFS"]?["Layers"]?.Select(t => t.ToString()).ToList() ?? [],
            Raw = item.ToString(Formatting.Indented)
        };

        if (config?["Labels"] is JObject labels)
        {
            foreach (var label in labels.Properties())
            {
                detail.Labels[label.Name] = label.Value.ToString();
            }
        }

        return detail;
    }

    public async Task<List<string>> GetContainersUsingImageAsync(string reference)
    {
        EnsureValidReference(reference);

        var result = await RunDockerAsync(
            ["ps", "-a", "--filter", $"ancestor={reference.Trim()}", "--format", "{{.Names}}"],
            TimeSpan.FromSeconds(20));

        if (result.ExitCode != 0) return [];

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToList();
    }

    #endregion

    #region 拉取

    public Task<string> StartPullAsync(DockerPullRequest request)
    {
        var image = DockerImageResolver.NormalizeReference(DockerImageResolver.Resolve(request.Image));
        EnsureValidReference(image);

        var platform = request.Platform?.Trim();

        // 同一镜像已有进行中的任务则直接复用
        if (_runningPulls.TryGetValue(image, out var existingTaskId))
        {
            var existing = GetPullTask(existingTaskId);
            if (existing is { Status: "pending" or "processing" })
            {
                return Task.FromResult(existingTaskId);
            }

            _runningPulls.TryRemove(image, out _);
        }

        var taskId = Guid.NewGuid().ToString("N");
        var status = new DockerPullTaskStatus
        {
            TaskId = taskId,
            Status = "pending",
            Progress = 0,
            Image = image,
            Message = "任务已排队，准备开始拉取..."
        };

        SaveTask(status);
        _runningPulls[image] = taskId;

        _ = Task.Run(() => PerformPullAsync(status, platform));

        return Task.FromResult(taskId);
    }

    public DockerPullTaskStatus? GetPullTask(string taskId)
    {
        return _cache.TryGetValue(PullTaskKeyPrefix + taskId, out DockerPullTaskStatus? status) ? status : null;
    }

    private async Task PerformPullAsync(DockerPullTaskStatus status, string? platform)
    {
        var layers = new ConcurrentDictionary<string, string>();
        var errorBuffer = new StringBuilder();

        try
        {
            status.Status = "processing";
            status.Message = $"正在拉取 {status.Image} ...";
            SaveTask(status);

            var args = new List<string> { "pull" };
            if (!string.IsNullOrEmpty(platform))
            {
                args.Add("--platform");
                args.Add(platform);
            }

            args.Add(status.Image);

            _logger.LogInformation("[Docker-Image] 开始拉取镜像 {Image} (任务 {TaskId})", status.Image, status.TaskId);

            var result = await Cli.Wrap("docker")
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                {
                    AppendLog(status, line);
                    UpdateLayerProgress(status, layers, line);
                    SaveTask(status);
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return;
                    errorBuffer.AppendLine(line);
                    AppendLog(status, line);
                    SaveTask(status);
                }))
                .ExecuteAsync();

            if (result.ExitCode == 0)
            {
                status.Status = "success";
                status.Progress = 100;
                status.Message = $"{status.Image} 拉取完成";
                _logger.LogInformation("[Docker-Image] 镜像 {Image} 拉取完成", status.Image);
            }
            else
            {
                status.Status = "error";
                status.Message = $"拉取失败：{TrimError(errorBuffer.ToString())}";
                _logger.LogWarning("[Docker-Image] 镜像 {Image} 拉取失败: {Error}", status.Image, errorBuffer.ToString().Trim());
            }
        }
        catch (Exception ex)
        {
            status.Status = "error";
            status.Message = IsDockerMissing(ex)
                ? "拉取失败：未找到 docker 命令，请确认宿主机已安装 Docker"
                : $"拉取异常：{ex.Message}";
            _logger.LogError(ex, "[Docker-Image] 镜像 {Image} 拉取过程异常", status.Image);
        }
        finally
        {
            SaveTask(status);
            _runningPulls.TryRemove(status.Image, out _);
        }
    }

    /// <summary>
    /// 依据逐层状态估算整体进度
    /// </summary>
    private static void UpdateLayerProgress(DockerPullTaskStatus status,
        ConcurrentDictionary<string, string> layers, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var match = LayerLinePattern.Match(line.Trim());
        if (!match.Success)
        {
            status.Message = line.Trim();
            return;
        }

        layers[match.Groups[1].Value] = match.Groups[2].Value.Trim();

        var done = layers.Values.Count(v =>
            v.StartsWith("Pull complete", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("Already exists", StringComparison.OrdinalIgnoreCase));

        status.Progress = layers.IsEmpty ? 5 : Math.Min(99, done * 100 / layers.Count);
        status.Message = $"正在拉取 {status.Image}（{done}/{layers.Count} 层完成）";
    }

    private static void AppendLog(DockerPullTaskStatus status, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        status.Logs.Add(line.TrimEnd());
        if (status.Logs.Count > MaxLogLines)
        {
            status.Logs.RemoveRange(0, status.Logs.Count - MaxLogLines);
        }
    }

    private void SaveTask(DockerPullTaskStatus status)
    {
        _cache.Set(PullTaskKeyPrefix + status.TaskId, status, TaskRetention);
    }

    #endregion

    #region 删除与清理

    public async Task<DockerOperationResult> RemoveImageAsync(DockerImageDeleteRequest request)
    {
        var reference = DockerImageResolver.Resolve(request.Reference).Trim();
        EnsureValidReference(reference);

        var args = new List<string> { "image", "rm" };
        if (request.Force) args.Add("--force");
        if (request.NoPrune) args.Add("--no-prune");
        args.Add(reference);

        var result = await RunDockerAsync(args, TimeSpan.FromMinutes(2));

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("[Docker-Image] 已删除镜像 {Reference}", reference);
            return new DockerOperationResult
            {
                Success = true,
                Message = "镜像已删除",
                Output = result.StandardOutput.Trim()
            };
        }

        _logger.LogWarning("[Docker-Image] 删除镜像 {Reference} 失败: {Error}", reference, result.StandardError.Trim());
        return new DockerOperationResult
        {
            Success = false,
            Message = TrimError(BuildErrorMessage(result, "删除失败")),
            Output = result.StandardError.Trim()
        };
    }

    public async Task<DockerOperationResult> PruneImagesAsync()
    {
        var result = await RunDockerAsync(["image", "prune", "-f"], TimeSpan.FromMinutes(5));

        if (result.ExitCode == 0)
        {
            var output = result.StandardOutput.Trim();
            _logger.LogInformation("[Docker-Image] 已清理悬空镜像: {Output}", output);

            var reclaimed = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(l => l.Contains("Total reclaimed space", StringComparison.OrdinalIgnoreCase))
                ?.Trim();

            return new DockerOperationResult
            {
                Success = true,
                Message = string.IsNullOrEmpty(reclaimed) ? "清理完成" : $"清理完成，{reclaimed}",
                Output = output
            };
        }

        return new DockerOperationResult
        {
            Success = false,
            Message = TrimError(BuildErrorMessage(result, "清理失败")),
            Output = result.StandardError.Trim()
        };
    }

    public async Task<DockerOperationResult> TagImageAsync(DockerImageTagRequest request)
    {
        var source = DockerImageResolver.Resolve(request.Source).Trim();
        var target = DockerImageResolver.NormalizeReference(request.Target);

        EnsureValidReference(source);
        EnsureValidReference(target);

        var result = await RunDockerAsync(["tag", source, target], TimeSpan.FromSeconds(30));

        if (result.ExitCode == 0)
        {
            _logger.LogInformation("[Docker-Image] 已为 {Source} 添加标签 {Target}", source, target);
            return new DockerOperationResult { Success = true, Message = $"已添加标签 {target}" };
        }

        return new DockerOperationResult
        {
            Success = false,
            Message = TrimError(BuildErrorMessage(result, "添加标签失败")),
            Output = result.StandardError.Trim()
        };
    }

    #endregion

    #region 通用

    private static void EnsureValidReference(string? reference)
    {
        if (!DockerImageResolver.IsValidReference(reference))
        {
            throw new ArgumentException("镜像名称包含非法字符或格式不正确");
        }
    }

    private static async Task<BufferedCommandResult> RunDockerAsync(IEnumerable<string> args, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        return await Cli.Wrap("docker")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cts.Token);
    }

    private static string BuildErrorMessage(BufferedCommandResult result, string fallback)
    {
        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return string.IsNullOrWhiteSpace(error) ? fallback : error.Trim();
    }

    /// <summary>
    /// docker 报错截断
    /// </summary>
    private static string TrimError(string error)
    {
        var text = error.Replace("\r", "").Trim();
        if (text.Length == 0) return "未知错误";

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var message = string.Join(" | ", lines.TakeLast(3)).Trim();

        return message.Length > 500 ? message[..500] + "..." : message;
    }

    #endregion
}
