using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Services.PluginsService;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Controllers.NodesControllers;

[ApiController, Route("api/node/sync"), Authorize(Roles = "admin")]
public sealed class NodeSyncController(NodeSyncService sync) : ControllerBase
{
    [HttpPost("preview")]
    public Task<IActionResult> Preview([FromBody] NodeSyncRequest request) => Execute(async () =>
        Ok(new ApiResponse<object> { Code = 200, Message = "同步预览已生成", Data = await sync.PreviewAsync(request, User, HttpContext.RequestAborted) }));

    [HttpPost]
    public Task<IActionResult> Start([FromBody] NodeSyncRequest request) => Execute(async () =>
        Ok(new ApiResponse<object> { Code = 200, Message = "同步任务已创建", Data = new { taskId = await sync.StartAsync(request, User, HttpContext.RequestAborted) } }));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (SyncConflictException ex) { return Conflict(new ApiResponse<object> { Code = 409, Message = ex.Message }); }
        catch (HttpRequestException ex)
        {
            var code = ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? 401 : 503;
            return StatusCode(code, new ApiResponse<object> { Code = code, Message = "同步预检失败: " + ex.Message });
        }
        catch (Exception ex) when (ex is InvalidDataException or OperationCanceledException or Newtonsoft.Json.JsonException)
        { return BadRequest(new ApiResponse<object> { Code = 400, Message = ex is OperationCanceledException ? "同步预检超时或已取消" : ex.Message }); }
    }
}
