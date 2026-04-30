using MSLX.SDK.Models.Instance;

namespace MSLX.SDK.IServices;

public interface IJavaScannerService
{
    /// <summary>
    /// 扫描 Java 环境
    /// </summary>
    /// <param name="forceRefresh">是否强制重新扫描（忽略缓存）</param>
    /// <returns></returns>
    Task<List<JavaInfo>> ScanJavaAsync(bool forceRefresh = false);
}