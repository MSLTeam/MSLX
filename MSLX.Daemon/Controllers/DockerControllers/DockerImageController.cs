using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Docker;

namespace MSLX.Daemon.Controllers.DockerControllers;

/// <summary>
/// 本地 Docker 镜像管理
/// </summary>
[ApiController]
[Route("api/docker")]
[Authorize(Roles = "admin")]
public class DockerImageController : ControllerBase
{
    private readonly IDockerService _docker;
    private readonly ILogger<DockerImageController> _logger;

    public DockerImageController(IDockerService docker, ILogger<DockerImageController> logger)
    {
        _docker = docker;
        _logger = logger;
    }

    /// <summary>
    /// Docker 环境探测
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] bool refresh = false)
    {
        var status = await _docker.GetStatusAsync(refresh);
        return Ok(ApiResponseService.Success(status));
    }

    /// <summary>
    /// 本地镜像列表
    /// </summary>
    /// <param name="dangling">是否包含无 tag 的悬空镜像</param>
    [HttpGet("images")]
    public async Task<IActionResult> GetImages([FromQuery] bool dangling = true)
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        try
        {
            var images = await _docker.ListImagesAsync(dangling);
            return Ok(ApiResponseService.Success(images));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Docker-Image] 获取镜像列表失败: {Message}", ex.Message);
            return StatusCode(500, ApiResponseService.Error(ex.Message, 500));
        }
    }

    /// <summary>
    /// MSLX 内置运行时镜像清单及本地存在情况
    /// </summary>
    [HttpGet("presets")]
    public async Task<IActionResult> GetPresets()
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        var presets = await _docker.ListPresetImagesAsync();
        return Ok(ApiResponseService.Success(presets));
    }

    /// <summary>
    /// 镜像详情
    /// </summary>
    [HttpGet("images/inspect")]
    public async Task<IActionResult> InspectImage([FromQuery] string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(ApiResponseService.BadRequest("镜像引用不能为空"));

        try
        {
            var detail = await _docker.InspectImageAsync(reference);
            if (detail == null) return NotFound(ApiResponseService.NotFound("镜像不存在"));

            return Ok(ApiResponseService.Success(detail));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseService.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponseService.Error(ex.Message, 500));
        }
    }

    /// <summary>
    /// 提交拉取镜像任务
    /// </summary>
    [HttpPost("images/pull")]
    public async Task<IActionResult> PullImage([FromBody] DockerPullRequest request)
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        try
        {
            var taskId = await _docker.StartPullAsync(request);
            _logger.LogInformation("[Docker-Image] 用户 {UserId} 提交拉取任务 {Image}",
                User?.FindFirst("UserId")?.Value ?? "unknown", request.Image);

            return Ok(ApiResponseService.Success<object>(new { TaskId = taskId }, "拉取任务已提交"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseService.BadRequest(ex.Message));
        }
    }

    /// <summary>
    /// 查询拉取任务进度
    /// </summary>
    [HttpGet("task/pull/{taskId}")]
    public IActionResult GetPullTask(string taskId)
    {
        var task = _docker.GetPullTask(taskId);
        if (task == null) return NotFound(ApiResponseService.NotFound("任务不存在或已过期"));

        return Ok(ApiResponseService.Success(task));
    }

    /// <summary>
    /// 删除镜像；被实例引用或存在关联容器时需要 force
    /// </summary>
    [HttpPost("images/delete")]
    public async Task<IActionResult> DeleteImage([FromBody] DockerImageDeleteRequest request)
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        if (string.IsNullOrWhiteSpace(request.Reference))
            return BadRequest(ApiResponseService.BadRequest("镜像引用不能为空"));

        try
        {
            if (!request.Force)
            {
                var blocker = await CheckImageInUseAsync(request.Reference);
                if (blocker != null) return Conflict(blocker);
            }

            var result = await _docker.RemoveImageAsync(request);
            _logger.LogInformation("[Docker-Image] 用户 {UserId} 删除镜像 {Reference} (force={Force}) 结果: {Success}",
                User?.FindFirst("UserId")?.Value ?? "unknown", request.Reference, request.Force, result.Success);

            return result.Success
                ? Ok(ApiResponseService.Success(result, result.Message))
                : StatusCode(500, ApiResponseService.CreateResponse(500, result.Message, result));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseService.BadRequest(ex.Message));
        }
    }

    /// <summary>
    /// 清理悬空（无 tag）镜像
    /// </summary>
    [HttpPost("images/prune")]
    public async Task<IActionResult> PruneImages()
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        var result = await _docker.PruneImagesAsync();
        _logger.LogInformation("[Docker-Image] 用户 {UserId} 执行了悬空镜像清理",
            User?.FindFirst("UserId")?.Value ?? "unknown");

        return result.Success
            ? Ok(ApiResponseService.Success(result, result.Message))
            : StatusCode(500, ApiResponseService.CreateResponse(500, result.Message, result));
    }

    /// <summary>
    /// 为镜像添加标签
    /// </summary>
    [HttpPost("images/tag")]
    public async Task<IActionResult> TagImage([FromBody] DockerImageTagRequest request)
    {
        var status = await _docker.GetStatusAsync();
        if (!status.Available) return StatusCode(503, BuildUnavailableResponse(status));

        try
        {
            var result = await _docker.TagImageAsync(request);
            return result.Success
                ? Ok(ApiResponseService.Success(result, result.Message))
                : StatusCode(500, ApiResponseService.CreateResponse(500, result.Message, result));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponseService.BadRequest(ex.Message));
        }
    }

    /// <summary>
    /// 检查镜像是否被实例引用或存在关联容器，返回 409 响应体；无占用返回 null
    /// </summary>
    private async Task<object?> CheckImageInUseAsync(string reference)
    {
        var images = await _docker.ListImagesAsync();
        var target = images.FirstOrDefault(i =>
            string.Equals(i.Reference, reference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.ImageId, reference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.ShortId, reference, StringComparison.OrdinalIgnoreCase));

        var usedBy = target?.UsedBy ?? [];
        var containers = await _docker.GetContainersUsingImageAsync(reference);

        if (usedBy.Count == 0 && containers.Count == 0) return null;

        var reasons = new List<string>();
        if (usedBy.Count > 0)
        {
            reasons.Add($"被 {usedBy.Count} 个实例引用（{string.Join("、", usedBy.Select(u => $"#{u.InstanceId} {u.InstanceName}"))}）");
        }

        if (containers.Count > 0)
        {
            reasons.Add($"存在 {containers.Count} 个关联容器（{string.Join("、", containers)}）");
        }

        return ApiResponseService.CreateResponse<object>(409,
            $"该镜像{string.Join("，且", reasons)}，如仍要删除请使用强制删除",
            new { UsedBy = usedBy, Containers = containers });
    }

    private static object BuildUnavailableResponse(DockerEnvStatus status)
    {
        var message = status.ErrorType switch
        {
            "notInstalled" => "未检测到 docker 命令，请先在宿主机安装 Docker",
            "sockNotMounted" => "MSLX 运行在容器内但未挂载 /var/run/docker.sock，请参考文档挂载后重启容器：https://mslx.mslmc.cn/docs/install/docker/",
            "permissionDenied" => "当前用户无权访问 Docker 守护进程，请将运行用户加入 docker 组或以更高权限运行",
            "daemonUnreachable" => "无法连接 Docker 守护进程，请确认 Docker 服务已启动",
            _ => "Docker 环境不可用"
        };

        return ApiResponseService.CreateResponse(503, message, status);
    }
}
