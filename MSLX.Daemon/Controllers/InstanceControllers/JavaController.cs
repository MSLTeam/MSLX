using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/java")]
[ApiController]
public class JavaController : ControllerBase
{
    private readonly JavaScannerService _javaScanner;

    public JavaController(JavaScannerService javaScanner)
    {
        _javaScanner = javaScanner;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetJavaList([FromQuery] bool refresh = false)
    {
        var javaList = await _javaScanner.ScanJavaAsync(refresh);
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = refresh ? "刷新成功" : "获取成功",
            Data = javaList
        });
    }
}