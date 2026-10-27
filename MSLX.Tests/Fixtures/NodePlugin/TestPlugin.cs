using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.SDK;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.NodeTestPlugin;

// Deliberately omits SupportsMultiNode: this exercises the old-plugin default implementation.
public sealed class TestPlugin : IPlugin
{
    public string Id => "mslx-plugin-node-fixture";
    public string Name => "多节点验证插件";
    public string Version =>
#if FIXTURE_V2
        "2.0.0";
#else
        "1.0.0";
#endif
    public void OnLoad()
    {
#if FIXTURE_LOAD_FAIL
        throw new InvalidOperationException("injected fixture version load failure");
#else
        if (Environment.GetEnvironmentVariable("MSLX_FIXTURE_FAIL_LOAD") == "1") throw new InvalidOperationException("injected fixture load failure");
#endif
    }
}

[ApiController, Route("api/plugins/mslx-plugin-node-fixture"), Authorize]
public sealed class FixtureController : ControllerBase
{
    [HttpGet("context")]
    public object Get() => new { code = 200, message = "成功", data = new { nodeId = Request.Headers["x-node-id"].ToString(), process = Environment.ProcessId } };

    [HttpPost("config"), Authorize(Roles = "admin")]
    public object Save([FromBody] JObject values)
    {
        MSLX.SDK.MSLX.Config.GetPluginConfig("mslx-plugin-node-fixture").WriteConfig(values);
        return new { code = 200, message = "配置已保存" };
    }
}
