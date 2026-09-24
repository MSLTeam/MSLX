namespace MSLX.SDK.IServices;

/// <summary>
/// 实例终端与交互服务
/// </summary>
public interface IInstanceConsoleService
{
    /// <summary>
    /// 向实例发送命令
    /// </summary>
    bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false);

    /// <summary>
    /// 发送原始 PTY 输入文本（自动按实例配置的输入编码转码）
    /// </summary>
    bool SendPtyInput(uint instanceId, string data);

    /// <summary>
    /// 发送原始 PTY 输入字节流
    /// </summary>
    bool SendPtyInput(uint instanceId, byte[] data);

    /// <summary>
    /// 调整 PTY 伪终端行列尺寸
    /// </summary>
    bool ResizePty(uint instanceId, int cols, int rows);

    /// <summary>
    /// 获取实例是否运行在 PTY 伪终端模式下
    /// </summary>
    bool IsServerPtyMode(uint instanceId);

    /// <summary>
    /// 获取实例日志历史
    /// </summary>
    List<string> GetLogs(uint instanceId);

    /// <summary>
    /// 获取 PTY 历史缓冲数据块
    /// </summary>
    List<string> GetPtyHistory(uint instanceId);
}
