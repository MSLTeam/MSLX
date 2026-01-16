using System.Dynamic;
using System.Security.Cryptography;
using System.Text;

namespace MSLX.Daemon.Utils;

public class FileUtils
{
    /// <summary>
    /// 校验文件的 SHA256 (异步)
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="expectedHash">期望的 Hash 字符串 (不区分大小写)</param>
    /// <returns>如果是 null/空 则跳过校验返回 true；匹配返回 true；否则 false</returns>
    public static async Task<bool> ValidateFileSha256Async(string filePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash)) return true;
        
        if (!File.Exists(filePath)) return false;

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = await sha256.ComputeHashAsync(stream);
            string actualHash = Convert.ToHexString(hashBytes);
            return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    // 路径安全检查工具
    public static (bool IsSafe, string FullPath, string Message) GetSafePath(string rootBase, string? relativePath)
    {
        if (string.IsNullOrEmpty(rootBase) || !Directory.Exists(rootBase))
        {
            return (false, string.Empty, "服务端根目录未配置或不存在");
        }

        try
        {
            string rootPath = Path.GetFullPath(rootBase);
            string reqPath = relativePath ?? "";
            string targetPath = Path.GetFullPath(Path.Combine(rootPath, reqPath));

            // 目标路径必须以根路径开头
            if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return (false, string.Empty, "禁止访问实例目录以外的资源");
            }

            return (true, targetPath, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, $"路径解析错误: {ex.Message}");
        }
    }

    // 是否二进制文件
    public static bool IsBinaryFile(string filePath)
    {
        const int bufferSize = 8192;
        var buffer = new char[bufferSize];

        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                int charsRead = reader.Read(buffer, 0, bufferSize);
                for (int i = 0; i < charsRead; i++)
                {
                    if (buffer[i] == '\0')
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return true;
        }

        return false;
    }
}

// 读取服务器配置文件
public class ServerPropertiesLoader : DynamicObject
{
    private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();

    /// <summary>
    /// 私有构造函数，防止外部直接实例化
    /// </summary>
    private ServerPropertiesLoader() { }

    /// <summary>
    /// 加载配置文件并返回动态对象
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>动态配置对象</returns>
    public static dynamic Load(string filePath,Encoding  encoding)
    {
        var instance = new ServerPropertiesLoader();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("配置文件未找到", filePath);
        }

        // 读取所有行
        var lines = File.ReadAllLines(filePath, encoding);

        foreach (var line in lines)
        {
            string trimLine = line.Trim();

            // 跳过注释 (#) 和空行
            if (string.IsNullOrEmpty(trimLine) || trimLine.StartsWith("#"))
            {
                continue;
            }

            // 按第一个 '=' 分割键值对
            int splitIndex = trimLine.IndexOf('=');
            if (splitIndex > 0)
            {
                string key = trimLine.Substring(0, splitIndex).Trim();
                string value = trimLine.Substring(splitIndex + 1).Trim();

                // 处理 Minecraft 特有的转义字符
                value = value.Replace(@"\:", ":");

                instance._properties[key] = value;
            }
        }

        return instance;
    }

    /// <summary>
    /// 重写动态成员获取逻辑
    /// </summary>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        string key = binder.Name;

        // 直接尝试获取
        if (_properties.TryGetValue(key, out string? value))
        {
            result = value;
            return true;
        }

        // 中划线转换
        string keyWithDashes = key.Replace("_", "-");
        if (_properties.TryGetValue(keyWithDashes, out string? valueWithDash))
        {
            result = valueWithDash;
            return true;
        }

        // 不知道哇
        result = "未知";
        return true;
    }

    /// <summary>
    /// 提供索引器访问方式：config["server-port"]
    /// </summary>
    public string this[string key]
    {
        get
        {
            return _properties.TryGetValue(key, out var val) ? val : "未知";
        }
    }
}