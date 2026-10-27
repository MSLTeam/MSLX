using MSLX.Daemon.Services.PluginsService;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services.NodeServices;

public static class NodeSettingsSync
{
    public static readonly IReadOnlyDictionary<string, JToken> Defaults = new Dictionary<string, JToken>(StringComparer.Ordinal)
    {
        ["allowNormalUserChangeUserName"] = true,
        ["allowNormalUserEditFrpConfig"] = true,
        ["allowNormalUserOfflineDownload"] = false,
        ["enableSsrfProtection"] = true,
        ["openWebConsoleOnLaunch"] = true,
        ["neoForgeInstallerMirrors"] = "MSL Mirrors",
        ["downloadThreadCount"] = 5
    };

    public static JObject Select(JObject config, IEnumerable<string> keys)
    {
        var selected = new JObject();
        foreach (var key in keys.Distinct().Order(StringComparer.Ordinal))
        {
            if (!Defaults.TryGetValue(key, out var fallback)) throw new InvalidDataException("该设置不允许同步: " + key);
            selected[key] = config[key]?.DeepClone() ?? fallback.DeepClone();
        }
        return selected;
    }

    public static void Validate(JObject patch)
    {
        if (!patch.HasValues) throw new InvalidDataException("未选择同步设置");
        foreach (var property in patch.Properties())
        {
            if (!Defaults.TryGetValue(property.Name, out var fallback)) throw new InvalidDataException("该设置不允许同步: " + property.Name);
            if (property.Value.Type != fallback.Type) throw new InvalidDataException("设置类型错误: " + property.Name);
            if (property.Name == "downloadThreadCount" && (!int.TryParse(property.Value.ToString(), out var count) || count is < 1 or > 8))
                throw new InvalidDataException("下载线程数量必须在 1-8 之间");
            if (property.Name == "neoForgeInstallerMirrors" && property.Value.ToString() is not ("Official" or "MSL Mirrors" or "MSL Mirrors Backup"))
                throw new InvalidDataException("安装镜像源参数错误");
        }
    }

    public static string Revision(JObject values) => PluginPackageService.ConfigHash(Select(values, values.Properties().Select(p => p.Name)));
}
