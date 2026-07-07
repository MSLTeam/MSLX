namespace MSLX.SDK.Models.Instance;

public class PythonInfo
{
    public string Path { get; set; } = string.Empty;      // python 可执行文件路径或命令
    public string Version { get; set; } = string.Empty;   // 版本号，如 3.11.5
    public bool HasMcdr { get; set; }                     // 是否已安装 MCDReforged
    public string? McdrVersion { get; set; }              // MCDR 版本(若已安装)
}
