using System.Security.Cryptography;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK;
using MSLX.SDK.Models.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services.PluginsService;

public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string MinSdkVersion { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Pending { get; set; }
    public string PackageHash { get; set; } = "";
    public string? ConfigHash { get; set; }
    public string Revision => PluginPackageService.Hash($"{Id}:{PackageHash}:{ConfigHash}:{Enabled}:{Pending}");
    [JsonIgnore] public string DllPath { get; set; } = "";
}

public sealed class PluginPackageService(PluginManager manager, ILogger<PluginPackageService> logger)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string FileHash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static string ConfigHash(JObject config) => Hash(config.ToString(Formatting.None));
    public static void ValidateId(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$"))
            throw new InvalidDataException("插件 ID 无效");
    }
    public static string ConfigPath(string id) { ValidateId(id); return Path.Combine(IConfigBase.GetAppDataPath(), "PluginsData", id, "Config.json"); }
    public static JObject? ReadConfig(string id) => File.Exists(ConfigPath(id)) ? IConfigBase.GetPluginConfigService(id).ReadConfig() : null;

    public string? Find(string id)
    {
        ValidateId(id);
        var loaded = manager.Plugins.FirstOrDefault(p => p.Metadata.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (loaded != null) return loaded.DllPath;
        var root = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "*.dll").FirstOrDefault(path =>
            manager.GetPluginMetadata(path)?.Id.Equals(id, StringComparison.OrdinalIgnoreCase) == true);
    }

    public PluginManifest? Describe(string id)
    {
        var path = Find(id);
        if (path == null) return null;
        var metadata = manager.GetPluginMetadata(path, true) ?? throw new InvalidDataException("插件元数据无效");
        var disabled = File.Exists(path + ".disabled");
        return new PluginManifest
        {
            Id = metadata.Id, Version = metadata.Version, MinSdkVersion = metadata.MinSDKVersion, DllPath = path, Enabled = !disabled,
            Pending = File.Exists(path + ".new") || File.Exists(path + ".delete") ||
                (!disabled && !manager.Plugins.Any(p => p.DllPath.Equals(path, StringComparison.OrdinalIgnoreCase))) ||
                (disabled && manager.Plugins.Any(p => p.DllPath.Equals(path, StringComparison.OrdinalIgnoreCase))),
            PackageHash = FileHash(path), ConfigHash = ReadConfig(metadata.Id) is { } config ? ConfigHash(config) : null
        };
    }

    public async Task<PluginManifest> InstallAsync(string tempPath, UploadPluginRequest request, CancellationToken cancellationToken)
    {
        var metadata = manager.GetPluginMetadata(tempPath, true) ?? throw new InvalidDataException("上传的文件不是有效的 MSLX 插件");
        ValidateCompatibility(metadata);
        if (request.ExpectedPluginId != null && !metadata.Id.Equals(request.ExpectedPluginId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("上传的插件 ID 与预期不一致");
        if (request.PackageHash != null && !FileHash(tempPath).Equals(request.PackageHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件程序校验失败，请重新上传");
        var gate = manager.GetOperationLock(metadata.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = Describe(metadata.Id);
            if (!request.Overwrite && current != null) throw new InvalidDataException("插件已存在，未允许覆盖");
            if (request.ExpectedRevision != null && request.ExpectedRevision != (current?.Revision ?? "missing"))
                throw new SyncConflictException("目标插件已变化，请重新预览");
            if (request.ExpectedRevision != null && current?.Pending == true)
                throw new SyncConflictException("目标插件有待处理操作，请先完成安装或删除");
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
            Directory.CreateDirectory(root);
            var name = Path.GetFileName(request.FileName ?? metadata.Id + ".dll");
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) name = metadata.Id + ".dll";
            var path = current?.DllPath ?? Path.Combine(root, name);
            // 文件名相同的处理
            if (current == null && File.Exists(path)) throw new InvalidDataException("插件文件名已被其他插件占用");
            var wasLoaded = manager.Plugins.Any(p => p.DllPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            var wasDisabled = File.Exists(path + ".disabled");
            var configPath = ConfigPath(metadata.Id);
            var hadConfig = File.Exists(configPath);
            JObject? oldConfig = null;
            var backup = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "PluginInstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            if (File.Exists(path)) File.Copy(path, Path.Combine(backup, "plugin.dll"));
            var pdb = Path.ChangeExtension(path, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(backup, "plugin.pdb"));
            var changed = false;
            var keepBackup = false;
            try
            {
                if (wasLoaded && !manager.UnloadPlugin(path)) throw new IOException("旧插件卸载失败，未替换插件文件");
                oldConfig = ReadConfig(metadata.Id);
                changed = true;
                File.WriteAllText(path + ".disabled", "");
                File.Copy(tempPath, path, true);
                if (File.Exists(pdb)) File.Delete(pdb);
                if (request.Config != null) IConfigBase.GetPluginConfigService(metadata.Id).WriteConfig(request.Config);
                var enabled = request.Enabled ?? !wasDisabled;
                if (enabled)
                {
                    File.Delete(path + ".disabled");
                    if (!manager.LoadPlugin(path)) throw new IOException("插件热加载失败，已尝试恢复原版本，请查看节点日志");
                }
                return Describe(metadata.Id)!;
            }
            catch (Exception original)
            {
                if (!changed)
                {
                    if (wasLoaded && !manager.Plugins.Any(p => p.DllPath.Equals(path, StringComparison.OrdinalIgnoreCase))) manager.LoadPlugin(path);
                    throw;
                }
                try
                {
                    if (manager.Plugins.Any(p => p.DllPath.Equals(path, StringComparison.OrdinalIgnoreCase)) && !manager.UnloadPlugin(path))
                        throw new IOException("无法卸载失败的新插件");
                    var oldDll = Path.Combine(backup, "plugin.dll");
                    if (File.Exists(oldDll)) File.Copy(oldDll, path, true); else if (File.Exists(path)) File.Delete(path);
                    var oldPdb = Path.Combine(backup, "plugin.pdb");
                    if (File.Exists(oldPdb)) File.Copy(oldPdb, pdb, true); else if (File.Exists(pdb)) File.Delete(pdb);
                    if (request.Config != null)
                    {
                        IConfigBase.GetPluginConfigService(metadata.Id).WriteConfig(oldConfig ?? new JObject());
                        if (!hadConfig && File.Exists(configPath)) File.Delete(configPath);
                    }
                    if (wasDisabled) File.WriteAllText(path + ".disabled", ""); else File.Delete(path + ".disabled");
                    if (wasLoaded && !manager.LoadPlugin(path)) throw new IOException("原插件重新加载失败");
                }
                catch (Exception rollback)
                {
                    keepBackup = true;
                    logger.LogError(rollback, "插件回滚失败: {PluginId}，备份保留于 {Backup}", metadata.Id, backup);
                    throw new IOException($"{original.Message}；恢复失败，插件备份已保留，请查看节点日志", rollback);
                }
                throw;
            }
            finally
            {
                // Keep the backup if a rollback itself failed.
                if (!keepBackup && Directory.Exists(backup))
                {
                    try { Directory.Delete(backup, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "清理插件临时备份失败: {PluginId}", metadata.Id); }
                }
            }
        }
        finally { gate.Release(); }
    }

    public static void ValidateCompatibility(IPlugin metadata)
    {
        PluginManager.ValidateMetadata(metadata);
        var host = typeof(PluginManager).Assembly.GetName().Version!;
        if (!Version.TryParse(metadata.MinSDKVersion?.TrimStart('v', 'V'), out var minimum)) throw new InvalidDataException("插件最低节点版本声明无效");
        if (minimum > host)
            throw new InvalidDataException($"插件要求最低节点版本 v{minimum}，当前 v{host}");
    }
}

public sealed class SyncConflictException(string message) : Exception(message);
