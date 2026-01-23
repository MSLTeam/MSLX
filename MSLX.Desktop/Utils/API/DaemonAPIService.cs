using MSLX.Desktop.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils.API
{
    internal class DaemonAPIService
    {
        #region Get

        /// <summary>
        /// 异步请求 Daemon API
        /// </summary>
        /// <param name="path">API路径，如 "/notice"</param>
        /// <returns>HTTP响应对象</returns>
        public static async Task<HttpResponse> GetApiAsync(string path, object? queryParameters = null)
        {
            string url = ConfigStore.DaemonAddress;

            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;

            return await HttpService.GetAsync(
                url + path,
                queryParameters,
                headers => headers.Add("x-api-key", ConfigStore.DaemonApiKey),
                UAManager.UAType.MSLX);
        }

        /// <summary>
        /// 异步获取 Daemon API JSON 内容
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

        #endregion

        #region Post

        /// <summary>
        /// 异步请求 Daemon API（POST）
        /// </summary>
        /// <param name="path">API路径，如"/create"</param>
        /// <param name="contentType">内容类型，如Json</param>
        /// <param name="postData">Post内容</param>
        /// <returns></returns>
        public async static Task<HttpResponse> PostApiAsync(string path,object? query,HttpService.PostContentType contentType, object? postData = null)
        {
            string url = ConfigStore.DaemonAddress;
            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;
            return await HttpService.PostAsync(
                url + path,
                query,
                contentType,
                postData,
                headers => headers.Add("x-api-key", ConfigStore.DaemonApiKey),
                UAManager.UAType.MSLX);
        }

        #endregion

        #region 常用方法
        public static async Task<(bool IsSuccess, string Msg, JObject? Data)> VerifyDaemonApiKey()
        {
            var response = await GetApiAsync("/api/status");
            DialogService.DialogManager.DismissDialog();
            if (response.IsSuccess)
            {
                var jsonContent = JObject.Parse(response.Content);
                if (jsonContent["code"]?.Value<int>() == 200)
                {
                    string msg = jsonContent["message"]?.Value<string>() ?? "200";
                    var data = jsonContent["data"] as JObject;
                    return (true, msg, data);
                }
            }
            return (false, "API Key无效", null);
        }
        #endregion
    }
}
