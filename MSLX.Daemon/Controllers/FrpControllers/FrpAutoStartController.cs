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
            // 去重
            var distinctIds = req.FrpIds.Distinct().ToList();

            // 验证ID
            var notFoundIds = new List<int>();
            foreach (var id in distinctIds)
            {
                if (!IConfigBase.FrpList.IsFrpIdValid(id))
                {
                    notFoundIds.Add(id);
                }
            }

            if (notFoundIds.Count > 0)
            {
                return Ok(new ApiResponse<object>
                {
                    Code = 400,
                    Message = $"保存失败：以下 FRP ID 不存在: {string.Join(", ", notFoundIds)}",
                    Data = null
                });
            }

            IConfigBase.Config.WriteConfigKey(ConfigKey, JArray.FromObject(distinctIds));

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "修改成功"
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