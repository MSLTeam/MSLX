using MSLX.Desktop.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils.API
{
    internal class MSLAPIService
    {
        /// <summary>
        /// 异步请求 MSL API
        /// </summary>
        /// <param name="path">API路径，如 "/notice"</param>
        /// <returns>HTTP响应对象</returns>
        public static async Task<HttpResponse> GetApiAsync(string path, object? queryParameters = null)
        {
            string url = ConfigStore.APILink;

            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;

            return await HttpService.GetAsync(
                url + path,
                queryParameters,
                headers => headers.Add("DeviceID", ConfigStore.DeviceID),
                UAManager.UAType.MSLX);
        }

        /// <summary>
        /// 异步获取 MSL API JSON 内容
        /// </summary>
        /// <param name="path">API路径，如 "/notice"</param>
        /// <returns>解析后的JSON对象</returns>
        public static async Task<JObject> GetJsonContentAsync(string path, object? queryParameters = null)
        {
            var response = await GetApiAsync(path, queryParameters);

            if (!response.IsSuccess)
            {
                if (response.Exception != null)
                    throw response.Exception;

                throw new HttpRequestException(
                    $"API请求失败: {response.Content}",
                    new Exception(response.StatusCode.ToString()));
            }

            try
            {
                return JObject.Parse(response.Content);
            }
            catch (Exception ex)
            {
                throw new JsonException(
                    $"JSON解析失败: {response.Content}",
                    ex);
            }
        }

        /// <summary>
        /// 快速获取API返回Json里的数据内容
        /// </summary>
        /// <param name="path">路径，如“/notice”</param>
        /// <param name="dataKey">数据键名称，默认为“data”</param>
        /// <param name="queryParameters">query参数</param>
        /// <returns>Success：请求是否成功</returns>
        public async static Task<(bool Success, object? Data, string? Msg)> GetJsonDataAsync(string path, string dataKey = "data", Dictionary<string, string>? queryParameters = null)
        {
            var getResponse = await GetApiAsync(path, queryParameters);
            if (getResponse.IsSuccess)
            {
                var content = getResponse.Content;
                if (content == null)
                    return (false, null, "内容为空");
                var json = JObject.Parse(content);
                if (json["code"]?.ToString() != "200")
                    return (false, null, json["message"]?.ToString());
                return (true, json[dataKey], json["message"]?.ToString());
            }
            else
            {
                return (false, null, "请求错误！");
            }
        }
    }
}
