namespace MSLX.SDK.IServices;

/// <summary>
/// 实例备份服务
/// </summary>
public interface IInstanceBackupService
{
    /// <summary>
    /// 开始对实例进行备份
    /// </summary>
    bool StartBackupServer(uint instanceId);
}
