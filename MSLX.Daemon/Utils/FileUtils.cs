using System.Security.Cryptography;

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
}