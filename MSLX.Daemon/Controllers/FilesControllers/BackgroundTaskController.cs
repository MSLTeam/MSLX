using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using System.Security.Claims;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/tasks")]
public class BackgroundTaskController : ControllerBase
{
    private readonly BackgroundTaskManager _taskManager;

    public BackgroundTaskController(BackgroundTaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    [HttpGet]
    public IActionResult GetTasks([FromQuery] uint? instanceId = null)
    {
        var userId = User?.FindFirst("UserId")?.Value ?? "";
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var tasks = _taskManager.GetUserTasks(userId, instanceId);
        return Ok(new ApiResponse<object> { Code = 200, Data = tasks, Message = "" });
    }

    [HttpPost("{taskId}/cancel")]
    public IActionResult CancelTask(string taskId)
    {
        var userId = User?.FindFirst("UserId")?.Value ?? "";
        var isAdmin = User?.IsInRole("admin") ?? false;
        
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (_taskManager.CancelTask(taskId, userId, isAdmin))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "已发送取消请求" });
        }
        return BadRequest(new ApiResponse<object> { Code = 400, Message = "任务无法取消或没有权限" });
    }

    [HttpDelete("{taskId}")]
    public IActionResult DeleteTask(string taskId)
    {
        var userId = User?.FindFirst("UserId")?.Value ?? "";
        var isAdmin = User?.IsInRole("admin") ?? false;
        
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (_taskManager.DeleteTask(taskId, userId, isAdmin))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "删除成功" });
        }
        return BadRequest(new ApiResponse<object> { Code = 400, Message = "删除失败或任务尚未结束" });
    }

    [HttpPost("clear-finished")]
    public IActionResult ClearFinishedTasks()
    {
        var userId = User?.FindFirst("UserId")?.Value ?? "";
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        _taskManager.ClearFinished(userId);
        return Ok(new ApiResponse<object> { Code = 200, Message = "清理完成" });
    }
}
