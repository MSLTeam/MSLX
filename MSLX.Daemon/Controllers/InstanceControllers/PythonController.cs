using Microsoft.AspNetCore.Mvc;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/python")]
[ApiController]
public class PythonController : ControllerBase
{
    private readonly IPythonScannerService _pythonScanner;

    public PythonController(IPythonScannerService pythonScanner)
    {
        _pythonScanner = pythonScanner;
    }

    /// <summary>
    /// 扫描本机可用的 Python 环境(用于 MCDReforged 部署)。
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetPythonList([FromQuery] bool refresh = false)
    {
        var list = await _pythonScanner.ScanPythonAsync(refresh);
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = list
        });
    }

    /// <summary>
    /// 检测指定的 Python 命令/路径是否可用。
    /// </summary>
    [HttpGet("inspect")]
    public async Task<IActionResult> Inspect([FromQuery] string python)
    {
        if (string.IsNullOrWhiteSpace(python))
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "python 参数不能为空",
            });
        }

        var info = await _pythonScanner.InspectPythonAsync(python);
        if (info == null)
        {
            return Ok(new ApiResponse<object>
            {
                Code = 404,
                Message = $"无法运行 Python: {python}",
            });
        }

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "检测成功",
            Data = info
        });
    }
}
