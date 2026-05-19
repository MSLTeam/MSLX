namespace MSLX.SDK.Interfaces;

public interface IDownloadService
{
    /// <summary>
    /// 异步下载文件
    /// </summary>
    /// <param name="url">下载地址</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="onProgress">进度回调 (进度百分比, 当前速度)</param>
    /// <param name="progressIntervalMs">进度回调频率（毫秒）</param>
    /// <returns>元组：(是否成功, 错误信息)</returns>
    Task<(bool Success, string ErrorMessage)> DownloadFileAsync(
        string url, 
        string savePath, 
        Action<double, string>? onProgress = null, 
        int progressIntervalMs = 1000);
}