using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Frp;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FrpControllers;

[ApiController]
public class CreateFrpController : ControllerBase
{
    [HttpPost("api/frp/add")]
    public IActionResult CreateServer([FromBody] CreateFrpRequest request)
    {
        bool suc = ConfigServices.FrpList.CreateFrpConfig(request.name, request.provider, request.format,
            request.config);
        var response = new ApiResponse<JObject>
        {
            Code = suc ? 200 : 500,
            Message = suc ? $"隧道 {request.name} 成功添加！" : "隧道创建失败！", 
        };

        return Ok(response);
    }
}