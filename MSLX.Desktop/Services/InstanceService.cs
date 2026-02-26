using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSLX.Desktop.Services;


/// <summary>
/// 封装服务端实例的 API 操作
/// </summary>
public class InstanceService
{
    /// <summary>
    /// 获取单个实例信息
    /// </summary>
    public static async Task<(bool Success, InstanceModel.InstanceInfo? Info, string? Msg)> GetInstanceInfoAsync(int id)
    {
        var (success, data, msg) = await DaemonAPIService.GetJsonDataAsync(
            "/api/instance/info",
            queryParameters: new Dictionary<string, string> { { "id", id.ToString() } });

        if (!success || data == null)
            return (false, null, msg);

        try
        {
            var token = data as JToken ?? JToken.FromObject(data);
            var info = token.ToObject<InstanceModel.InstanceInfo>();
            return (true, info, msg);
        }
        catch (Exception ex)
        {
            return (false, null, $"解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 启动实例
    /// </summary>
    public static async Task<(bool Success, string? Msg)> StartInstanceAsync(int id)
        => await SendActionAsync(id, "start");

    /// <summary>
    /// 停止实例
    /// </summary>
    public static async Task<(bool Success, string? Msg)> StopInstanceAsync(int id)
        => await SendActionAsync(id, "stop");

    /// <summary>
    /// 备份实例存档
    /// </summary>
    public static async Task<(bool Success, string? Msg)> BackupInstanceAsync(int id)
        => await SendActionAsync(id, "backup");

    /// <summary>
    /// 获取实例通用设置
    /// </summary>
    public static async Task<(bool Success, InstanceModel.InstanceGeneralSettings? Settings, string? Msg)> GetGeneralSettingsAsync(int id)
    {
        var (success, data, msg) = await DaemonAPIService.GetJsonDataAsync($"/api/instance/settings/general/{id}");

        if (!success || data == null)
            return (false, null, msg);
        Debug.WriteLine($"Received general settings data: {data}");
        try
        {
            var token = data as JToken ?? JToken.FromObject(data);
            var settings = token.ToObject<InstanceModel.InstanceGeneralSettings>();
            return (true, settings, msg);
        }
        catch (Exception ex)
        {
            return (false, null, $"解析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 更新实例通用设置
    /// </summary>
    public static async Task<(bool Success, string? Msg)> UpdateGeneralSettingsAsync(int id, InstanceModel.InstanceGeneralSettings settings)
    {
        var response = await DaemonAPIService.PostApiAsync(
            $"/api/instance/settings/general/{id}",
            null,
            HttpService.PostContentType.Json,
            settings);

        if (!response.IsSuccess)
            return (false, response.Exception?.Message ?? "请求失败");

        try
        {
            var json = JObject.Parse(response.Content);
            bool ok = json["code"]?.ToString() == "200";
            return (ok, json["message"]?.ToString());
        }
        catch
        {
            return (false, "响应格式错误");
        }
    }

    // 发送Action命令----------
    // 公共版本
    public static async Task<(bool Success, string? Msg)> SendInstanceActionAsync(int id, string action, string? query = null)
        => await SendActionAsync(id, action, query);
    // 私有
    private static async Task<(bool Success, string? Msg)> SendActionAsync(int id, string action, string? query = null)
    {
        var response = await DaemonAPIService.PostApiAsync(
            "/api/instance/action",
            null,
            HttpService.PostContentType.Json,
            new { id, action = query == null ? action : $"{action}?{query}" });

        if (!response.IsSuccess)
            return (false, response.Exception?.Message ?? "请求失败");

        try
        {
            var json = JObject.Parse(response.Content);
            bool ok = json["code"]?.ToString() == "200";
            return (ok, json["message"]?.ToString());
        }
        catch
        {
            return (false, "响应格式错误");
        }
    }
}
