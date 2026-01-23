using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Frp;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FrpControllers;

[ApiController]
[Route("api/frp/autostart")]
public class FrpAutoStartController : ControllerBase
{
    private const string ConfigKey = "frpAutoStartList";

    [HttpGet]
    public IActionResult GetFrpAutoStartList()
    {
        var configValue = IConfigBase.Config.ReadConfigKey(ConfigKey);
        var list = configValue?.ToObject<List<int>>() ?? [];

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = list
        });
    }

    [HttpPost]
    public IActionResult UpdateFrpAutoStartList([FromBody] UpdateFrpAutoStartRequest req)
    {
        try
        {
            // 去重并过滤无效 ID
            var validIds = req.FrpIds
                .Distinct()
                .Where(id => IConfigBase.FrpList.IsFrpIdValid(id))
                .ToList();

            IConfigBase.Config.WriteConfigKey(ConfigKey, JArray.FromObject(validIds));

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = $"修改成功，已保存 {validIds.Count} 个有效配置",
                Data = validIds
            });
        }
        catch (Exception ex)
        {
            return Ok(new ApiResponse<object>
            {
                Code = 500,
                Message = $"保存配置失败: {ex.Message}",
                Data = null
            });
        }
    }
}