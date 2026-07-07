using MSLX.SDK.Models;

namespace MSLX.Daemon.Utils;

public static class ServerPropertiesPathUtils
{
    public const string DefaultServerPropertiesPath = "server.properties";
    public const string InvalidPathMessage = "server.properties 路径必须是实例目录内的相对路径";

    public static string NormalizeRelativePath(string? path)
    {
        return NormalizeRelativePath(path, DefaultServerPropertiesPath, InvalidPathMessage);
    }

    public static string NormalizeRelativePath(string? path, string defaultPath, string invalidPathMessage)
    {
        var value = string.IsNullOrWhiteSpace(path)
            ? defaultPath
            : path.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(value) || IsWindowsAbsolutePath(value))
        {
            throw new ArgumentException(invalidPathMessage);
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return defaultPath;
        }

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        if (segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(invalidFileNameChars) >= 0))
        {
            throw new ArgumentException(invalidPathMessage);
        }

        return string.Join('/', segments);
    }

    private static bool IsWindowsAbsolutePath(string path)
    {
        return path.Length >= 3 &&
               char.IsLetter(path[0]) &&
               path[1] == ':' &&
               path[2] == '/';
    }

    public static string ResolveFullPath(McServerInfo.ServerInfo server)
    {
        var relativePath = NormalizeRelativePath(server.ServerPropertiesPath);
        var check = FileUtils.GetSafePath(server.Base, relativePath);

        if (!check.IsSafe)
        {
            throw new ArgumentException(InvalidPathMessage);
        }

        return check.FullPath;
    }

    /// <summary>
    /// 解析 eula.txt 的完整路径。eula.txt 始终与 server.properties 位于同一目录，
    /// 以兼容 MCDR 等将真实服务端放入子目录(如 server/)的布局。
    /// 路径非法时回退到实例根目录。
    /// </summary>
    public static string ResolveEulaPath(McServerInfo.ServerInfo server)
    {
        try
        {
            var relativePath = NormalizeRelativePath(server.ServerPropertiesPath);
            var relativeDir = Path.GetDirectoryName(relativePath.Replace('/', Path.DirectorySeparatorChar));
            var eulaRelative = string.IsNullOrEmpty(relativeDir)
                ? "eula.txt"
                : Path.Combine(relativeDir, "eula.txt");

            var check = FileUtils.GetSafePath(server.Base, eulaRelative);
            if (check.IsSafe)
            {
                return check.FullPath;
            }
        }
        catch (ArgumentException)
        {
            // 路径非法，回退到实例根目录
        }

        return Path.Combine(server.Base, "eula.txt");
    }
}
