using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace MSLX.Daemon.Controllers.FrpControllers.Providers;

[ApiController]
[Route("api/frp/chmlfrp")]
[Authorize(Roles = "admin")]
public class ChmlFrpProxyController : ControllerBase
{
    private const string ChmlFrpApiBaseUrl = "https://cf-v2.uapis.cn";

    [HttpGet("userinfo")]
    public Task<IActionResult> GetUserInfo()
        => ForwardGetAsync($"{ChmlFrpApiBaseUrl}/userinfo");

    [HttpGet("tunnel")]
    public Task<IActionResult> GetTunnels()
        => ForwardGetAsync($"{ChmlFrpApiBaseUrl}/tunnel");

    [HttpGet("node")]
    public Task<IActionResult> GetNodes()
        => ForwardGetAsync($"{ChmlFrpApiBaseUrl}/node");

    [HttpGet("delete-tunnel")]
    public Task<IActionResult> DeleteTunnel([FromQuery] int tunnelId)
        => ForwardGetAsync($"{ChmlFrpApiBaseUrl}/delete_tunnel?tunnelid={tunnelId}");

    [HttpGet("tunnel-config")]
    public Task<IActionResult> GetTunnelConfig([FromQuery] string node, [FromQuery] string tunnelName)
        => ForwardGetAsync(
            $"{ChmlFrpApiBaseUrl}/tunnel_config?node={Uri.EscapeDataString(node)}&tunnel_names={Uri.EscapeDataString(tunnelName)}");

    [HttpPost("create-tunnel")]
    public async Task<IActionResult> CreateTunnel([FromBody] JsonElement body)
    {
        var response = await GeneralApi.PostAsync(
            $"{ChmlFrpApiBaseUrl}/create_tunnel",
            HttpService.PostContentType.Json,
            body,
            BuildProxyHeaders());

        return BuildActionResult(response);
    }

    private async Task<IActionResult> ForwardGetAsync(string url)
    {
        var response = await GeneralApi.GetAsync(url, headers: BuildProxyHeaders());
        return BuildActionResult(response);
    }

    private Dictionary<string, string> BuildProxyHeaders()
    {
        var headers = new Dictionary<string, string>();
        if (Request.Headers.TryGetValue("X-Chmlfrp-Authorization", out StringValues authorization)
            && !StringValues.IsNullOrEmpty(authorization))
        {
            headers["Authorization"] = authorization.ToString();
        }
        return headers;
    }

    private IActionResult BuildActionResult(HttpService.HttpResponse response)
    {
        if (response.IsSuccessStatusCode)
        {
            return Ok(new ApiResponse<object?>
            {
                Code = 200,
                Message = "请求成功",
                Data = ParseResponseContent(response.Content)
            });
        }

        var statusCode = response.StatusCode > 0 ? response.StatusCode : 502;
        return StatusCode(statusCode, new ApiResponse<object?>
        {
            Code = statusCode,
            Message = ExtractErrorMessage(response.Content, response.ResponseException),
            Data = ParseResponseContent(response.Content)
        });
    }

    private static object? ParseResponseContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JToken.Parse(content);
        }
        catch
        {
            return content;
        }
    }

    private static string ExtractErrorMessage(string? content, object? responseException)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var token = JToken.Parse(content);
                var message = token["msg"]?.ToString()
                              ?? token["message"]?.ToString()
                              ?? token["error_description"]?.ToString()
                              ?? token["error"]?.ToString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }
            }
        }

        return responseException?.ToString() ?? "请求失败";
    }
}
