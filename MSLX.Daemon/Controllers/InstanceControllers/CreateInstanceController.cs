using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;
using MSLX.SDK.Models.Tasks;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[ApiController]
[Route("api/instance")]
[Authorize(Roles = "admin")]
public class CreateInstanceController : ControllerBase
{
    private readonly IBackgroundTaskQueue<CreateServerTask> _taskQueue;
    private readonly IMCServerService _mcServerService;
    private readonly CreationTaskTracker _taskTracker;
    private readonly BackgroundTaskManager _taskManager;

    public CreateInstanceController(
        IBackgroundTaskQueue<CreateServerTask> taskQueue, 
        IMCServerService mcServerService, 
        CreationTaskTracker taskTracker,
        BackgroundTaskManager taskManager)
    {
        _taskQueue = taskQueue;
        _mcServerService = mcServerService;
        _taskTracker = taskTracker;
        _taskManager = taskManager;
    }

    [HttpPost("createServer")]
    public async Task<IActionResult> CreateServer([FromBody] CreateServerRequest request)
    {
        var serverId = IConfigBase.ServerList.GenerateServerId();
        var userId = User?.FindFirst("UserId")?.Value ?? "";

        // 创建全局后台任务
        var (bgTask, _) = _taskManager.CreateTask(
            userId, 
            serverId, 
            MSLX.SDK.Models.Files.TaskType.CreateServer, 
            $"创建实例 {request.name}", 
            request.core ?? request.name
        );

        // 创建一个任务对象
        var task = new CreateServerTask
        {
            ServerId = serverId.ToString(), 
            Request = request,
            BackgroundTaskId = bgTask.Id,
            UserId = userId
        };
        
        await _taskQueue.QueueTaskAsync(task); // 添加任务到后台队列
        
        var response = new ApiResponse<JObject>
        {
            Code = 200,
            Message = "创建任务已提交", 
            Data = new JObject
            {
                ["serverId"] = serverId
            }
        };

        return Ok(response);
    }
    
    [HttpPost("delete")]
    public IActionResult DeleteServer([FromBody] DeleteServerRequest request)
    {
        if (_mcServerService.IsServerRunning(request.Id))
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "服务器实例正在运行，请先停止再删除！",
            });
        }
        bool suc = IConfigBase.ServerList.DeleteServer(request.Id,request.DeleteFiles ?? false);
        var response = new ApiResponse<object>
        {
            Code = suc ? 200 : 400,
            Message = suc ? $"服务器实例 {request.Id} 删除成功！" : $"服务器实例 {request.Id} 删除失败！", 
        };

        return suc ? Ok(response) : BadRequest(response);
    }

    [HttpPost("cancelCreation")]
    public IActionResult CancelCreation([FromBody] CancelCreationRequest request)
    {
        bool cancelled = _taskTracker.TryCancel(request.ServerId, request.CleanupFiles);

        var response = new ApiResponse<object>
        {
            Code = cancelled ? 200 : 404,
            Message = cancelled ? "取消信号已发送" : $"未找到正在执行的创建任务 (ServerId: {request.ServerId})"
        };

        return cancelled ? Ok(response) : BadRequest(response);
    }
}