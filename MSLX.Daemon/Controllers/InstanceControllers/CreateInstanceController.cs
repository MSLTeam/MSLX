using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Models.Instance; 
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Utils.BackgroundTasks; 

namespace MSLX.Daemon.Controllers.InstanceControllers;

[ApiController]
public class CreateInstanceController : ControllerBase
{
    private readonly IBackgroundTaskQueue _taskQueue; // 注入后台队列

    // 注入队列
    public CreateInstanceController(IBackgroundTaskQueue taskQueue)
    {
        _taskQueue = taskQueue;
    }

    [HttpPost("api/instance/createServer")]
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
}