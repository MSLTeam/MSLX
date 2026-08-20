using System;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Utils;

public static class DownloadMirrorHelper
{
    /// <summary>
    /// 根据原始下载链接尝试获取镜像加速链接
    /// </summary>
    /// <param name="originalUrl">原始下载链接</param>
    /// <returns>镜像链接，如果不支持镜像加速则返回 null</returns>
    public static string? GetMirrorUrl(string originalUrl)
    {
        if (string.IsNullOrEmpty(originalUrl)) return null;

        // Modrinth Mirror
        const string modrinthPrefix = "https://cdn.modrinth.com/data/";
        if (originalUrl.StartsWith(modrinthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "https://v2.mirrors.mslmc.cn/resources/modrinth/" + originalUrl.Substring(modrinthPrefix.Length);
        }

        // CurseForge Mirror (仅支持 edge.forgecdn.net)
        // Format: https://edge.forgecdn.net/files/8649/107/ServerFiles-8.0.zip
        var match = Regex.Match(originalUrl, @"https://edge\.forgecdn\.net/files/(\d+)/(\d+)/(.*)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string part1 = match.Groups[1].Value;
            string part2 = match.Groups[2].Value;
            string fileName = match.Groups[3].Value;
            return $"https://v2.mirrors.mslmc.cn/resources/curseforge/{part1}/{part2}/{fileName}";
        }

        return null;
    }
}
