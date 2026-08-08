using MSLX.SDK.Models.Docker;

namespace MSLX.SDK.IServices;

public interface IDockerService
{
    /// <summary>
    /// 探测 Docker 环境是否可用
    /// </summary>
    /// <param name="forceRefresh">是否忽略缓存重新探测</param>
    Task<DockerEnvStatus> GetStatusAsync(bool forceRefresh = false);

    /// <summary>
    /// 获取本地镜像列表
    /// </summary>
    /// <param name="includeDangling">是否包含无 tag 的悬空镜像</param>
    Task<List<DockerImageInfo>> ListImagesAsync(bool includeDangling = true);

    /// <summary>
    /// 获取 MSLX 内置运行时镜像清单及本地存在状态
    /// </summary>
    Task<List<DockerPresetImage>> ListPresetImagesAsync();

    /// <summary>
    /// 查看镜像详情
    /// </summary>
    Task<DockerImageDetail?> InspectImageAsync(string reference);

    /// <summary>
    /// 提交拉取镜像任务，返回任务 ID（相同镜像的进行中任务会被复用）
    /// </summary>
    Task<string> StartPullAsync(DockerPullRequest request);

    /// <summary>
    /// 查询拉取任务状态
    /// </summary>
    DockerPullTaskStatus? GetPullTask(string taskId);

    /// <summary>
    /// 删除镜像
    /// </summary>
    Task<DockerOperationResult> RemoveImageAsync(DockerImageDeleteRequest request);

    /// <summary>
    /// 清理悬空镜像
    /// </summary>
    Task<DockerOperationResult> PruneImagesAsync();

    /// <summary>
    /// 给镜像新增 tag
    /// </summary>
    Task<DockerOperationResult> TagImageAsync(DockerImageTagRequest request);

    /// <summary>
    /// 查询占用了指定镜像的容器名称列表
    /// </summary>
    Task<List<string>> GetContainersUsingImageAsync(string reference);

    /// <summary>
    /// 检查一个或多个镜像是否有远程更新
    /// </summary>
    Task<List<DockerImageCheckUpdateItem>> CheckImagesUpdateAsync(List<string>? references);
}
