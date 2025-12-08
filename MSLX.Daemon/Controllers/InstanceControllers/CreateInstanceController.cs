using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Models.Instance; 
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.BackgroundTasks; 

namespace MSLX.Daemon.Controllers.InstanceControllers;

[ApiController]
[Route("api/instance")]
public class CreateInstanceController : ControllerBase
{
    private readonly IBackgroundTaskQueue _taskQueue; // 注入后台队列
    private readonly MCServerService _mcServerService;

    // 注入队列
    public CreateInstanceController(IBackgroundTaskQueue taskQueue,MCServerService mcServerService)
    {
        _taskQueue = taskQueue;
        _mcServerService = mcServerService;
    }

    [HttpPost("createServer")]
    public async Task<IActionResult> CreateServer([FromBody] CreateServerRequest request)
    {
        var serverId = ConfigServices.ServerList.GenerateServerId();

        // 创建一个任务对象
        var task = new CreateServerTask
        {
            ServerId = serverId.ToString(), 
            Request = request
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
        bool suc = ConfigServices.ServerList.DeleteServer(request.Id,request.DeleteFiles ?? false);
        var response = new ApiResponse<object>
        {
            Code = suc ? 200 : 400,
            Message = suc ? $"服务器实例 {request.Id} 删除成功！" : $"服务器实例 {request.Id} 删除失败！", 
        };

        return suc ? Ok(response) : BadRequest(response);
    }
}