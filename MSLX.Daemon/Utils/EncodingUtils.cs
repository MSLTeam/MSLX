using System.Text;

namespace MSLX.Daemon.Utils;

public static class EncodingUtils
{
    /// <summary>
    /// 按名称解析编码，无法识别时回退为无 BOM 的 UTF-8。
    /// UTF-8 会被特殊处理为无 BOM 版本。
    /// <see cref="CodePagesEncodingProvider"/>。
    /// </summary>
    /// <param name="encodingName">编码名称，空值直接返回无 BOM UTF-8</param>
    /// <param name="onFallback">发生回退时的回调（参数为原始编码名），可用于记录日志</param>
    public static Encoding GetEncodingOrDefault(string? encodingName, Action<string>? onFallback = null)
    {
        // 默认返回无 BOM 的 UTF-8
        if (string.IsNullOrWhiteSpace(encodingName))
            return new UTF8Encoding(false);

        try
        {
            var name = encodingName.Trim().ToLower();

            // 特殊处理 UTF-8，强制禁用 BOM
            if (name == "utf-8" || name == "utf8")
            {
                return new UTF8Encoding(false);
            }

            return Encoding.GetEncoding(name);
        }
        catch (Exception)
        {
            onFallback?.Invoke(encodingName);
            // 回退使用无 BOM 的 UTF-8
            return new UTF8Encoding(false);
        }
    }
}
