using MSLX.SDK.Models.Instance;

namespace MSLX.SDK.IServices;

public interface IPythonScannerService
{
    /// <summary>
    /// 扫描本机可用的 Python 环境(用于 MCDReforged 部署)。
    /// </summary>
    /// <param name="forceRefresh">是否强制重新扫描(忽略缓存)</param>
    Task<List<PythonInfo>> ScanPythonAsync(bool forceRefresh = false);

    /// <summary>
    /// 检测指定的 Python 命令/路径是否可用，并返回其信息(含是否安装 MCDR)。
    /// </summary>
    Task<PythonInfo?> InspectPythonAsync(string python);
}
