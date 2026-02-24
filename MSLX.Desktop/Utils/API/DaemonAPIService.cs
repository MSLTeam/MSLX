using MSLX.Desktop.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
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
        public static async Task<HttpResponse> GetApiAsync(string path, object? queryParameters = null, CancellationToken cancellationToken = default)
        {
            string url = ConfigStore.DaemonAddress;

            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;

            return await HttpService.GetAsync(
                url + path,
                queryParameters,
                headers => headers.Add("x-api-key", ConfigStore.DaemonApiKey),
                UAManager.UAType.MSLX,
                cancellationToken: cancellationToken);
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
                try
                {
                    var json = JObject.Parse(content);
                    if (json["code"]?.ToString() != "200")
                        return (false, null, json["message"]?.ToString());
                    return (true, json[dataKey], json["message"]?.ToString());
                }
                catch
                {
                    return (false, null, "API响应格式错误，转换Json失败！");
                }
            }
            else
            {
                return (false, null, "请求错误！\n" + getResponse.Exception?.Message);
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

        /// <summary>
        /// 专门用于文件上传的 POST 方法
        /// </summary>
        public static async Task<HttpResponse> PostMultipartAsync(
            string path,
            MultipartFormDataContent content)
        {
            string url = ConfigStore.DaemonAddress;

            if (!path.StartsWith("/"))
                path = "/" + path;

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", ConfigStore.DaemonApiKey);
            httpClient.DefaultRequestHeaders.Add("User-Agent", UAManager.GetUA(UAManager.UAType.MSLX));

            try
            {
                var response = await httpClient.PostAsync(url + path, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return new HttpResponse
                {
                    StatusCode = response.StatusCode,
                    Content = responseContent,
                    Exception = null
                };
            }
            catch (Exception ex)
            {
                return new HttpResponse
                {
                    StatusCode = 0,
                    Exception = ex
                };
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        #endregion

        #region 常用方法
        public static async Task<(bool IsSuccess, string Msg, JObject? Data)> VerifyDaemonApiKey(CancellationToken cancellationToken = default)
        {
            var response = await GetApiAsync("/api/status", cancellationToken: cancellationToken);

            Debug.WriteLine(ConfigStore.DaemonApiKey);
            Debug.WriteLine(response.StatusCode);
            Debug.WriteLine(response.Content);

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

        /// <summary>
        /// 创建服务器实例
        /// </summary>
        /// <param name="request">创建请求对象</param>
        /// <returns>成功标志、返回数据、消息</returns>
        public static async Task<(bool Success, JObject? Data, string? Message)> CreateServerInstanceAsync(object request)
        {
            try
            {
                var response = await PostApiAsync(
                    "/api/instance/createServer",
                    null,
                    HttpService.PostContentType.Json,
                    request
                );

                if (response.IsSuccess)
                {
                    if (string.IsNullOrEmpty(response.Content))
                    {
                        return (true, null, "创建成功");
                    }

                    try
                    {
                        var json = JObject.Parse(response.Content);
                        var code = json["code"]?.ToString();
                        var message = json["message"]?.ToString();
                        var data = json["data"] as JObject;

                        if (code == "200")
                        {
                            return (true, data, message);
                        }
                        else
                        {
                            return (false, null, message ?? "创建失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, null, $"解析响应失败: {ex.Message}");
                    }
                }
                else
                {
                    var errorMsg = response.Exception?.Message ?? response.Content ?? "HTTP请求失败";
                    return (false, null, errorMsg);
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"请求异常: {ex.Message}");
            }
        }

        #region 文件上传

        /// <summary>
        /// 初始化文件上传
        /// </summary>
        /// <returns>成功标志、uploadId、消息</returns>
        public static async Task<(bool Success, string? UploadId, string? Message)> InitFileUploadAsync()
        {
            try
            {
                var response = await PostApiAsync(
                    "/api/files/upload/init",
                    null,
                    HttpService.PostContentType.Json,
                    null
                );

                if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var code = json["code"]?.ToString();
                    var message = json["message"]?.ToString();

                    if (code == "200")
                    {
                        var uploadId = json["data"]?["uploadId"]?.ToString();
                        return (true, uploadId, message);
                    }
                    else
                    {
                        return (false, null, message ?? "初始化失败");
                    }
                }

                return (false, null, response.Exception?.Message ?? "请求失败");
            }
            catch (Exception ex)
            {
                return (false, null, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 上传文件分片
        /// </summary>
        /// <param name="uploadId">上传ID</param>
        /// <param name="index">分片索引</param>
        /// <param name="fileData">文件数据</param>
        /// <returns>成功标志、消息</returns>
        public static async Task<(bool Success, string? Message)> UploadFileChunkAsync(
            string uploadId,
            int index,
            byte[] fileData)
        {
            try
            {
                // 构建 multipart/form-data
                using var content = new MultipartFormDataContent
                {
                    { new StringContent(uploadId), "uploadId" }, // 添加 uploadId 字段
                    { new StringContent(index.ToString()), "index" } // 添加 index 字段
                };

                // 添加文件
                var fileContent = new ByteArrayContent(fileData);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", $"chunk_{index}");

                var response = await PostMultipartAsync(
                    $"/api/files/upload/chunk/{uploadId}",
                    content);

                if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var code = json["code"]?.ToString();
                    var message = json["message"]?.ToString();

                    if (code == "200")
                    {
                        return (true, message);
                    }
                    else
                    {
                        return (false, message ?? "上传失败");
                    }
                }

                return (false, response.Exception?.Message ?? "请求失败");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 完成文件上传
        /// </summary>
        /// <param name="uploadId">上传ID</param>
        /// <param name="totalChunks">总分片数</param>
        /// <returns>成功标志、消息</returns>
        public static async Task<(bool Success, string? Message)> FinishFileUploadAsync(
            string uploadId,
            int totalChunks)
        {
            try
            {
                var requestData = new { totalChunks = totalChunks };

                var response = await PostApiAsync(
                    $"/api/files/upload/finish/{uploadId}",
                    null,
                    HttpService.PostContentType.Json,
                    requestData
                );

                if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var code = json["code"]?.ToString();
                    var message = json["message"]?.ToString();

                    if (code == "200")
                    {
                        return (true, message);
                    }
                    else
                    {
                        return (false, message ?? "完成失败");
                    }
                }

                return (false, response.Exception?.Message ?? "请求失败");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除上传的临时文件
        /// </summary>
        /// <param name="uploadId">上传ID</param>
        /// <returns>成功标志、消息</returns>
        public static async Task<(bool Success, string? Message)> DeleteUploadAsync(string uploadId)
        {
            try
            {
                var response = await PostApiAsync(
                    $"/api/files/upload/delete/{uploadId}",
                    null,
                    HttpService.PostContentType.Json,
                    null
                );

                if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var code = json["code"]?.ToString();
                    var message = json["message"]?.ToString();

                    if (code == "200")
                    {
                        return (true, message);
                    }
                    else
                    {
                        return (false, message ?? "删除失败");
                    }
                }

                return (false, response.Exception?.Message ?? "请求失败");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查上传的压缩包包含的Jar包列表
        /// </summary>
        /// <param name="uploadId">上传ID</param>
        /// <returns>成功标志、Jar文件列表、消息</returns>
        public static async Task<(bool Success, List<string>? JarFiles, string? DetectedRoot, string? Message)> InspectUploadAsync(string uploadId)
        {
            try
            {
                var response = await GetApiAsync(
                    $"/api/files/upload/inspect/{uploadId}",
                    null
                );

                if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    var code = json["code"]?.ToString();
                    var message = json["message"]?.ToString();

                    if (code == "200")
                    {
                        var data = json["data"];
                        var jars = data?["jars"]?.ToObject<List<string>>();
                        var detectedRoot = data?["detectedRoot"]?.ToString();

                        return (true, jars, detectedRoot, message);
                    }
                    else
                    {
                        return (false, null, null, message ?? "检查失败");
                    }
                }

                return (false, null, null, response.Exception?.Message ?? "请求失败");
            }
            catch (Exception ex)
            {
                return (false, null, null, $"异常: {ex.Message}");
            }
        }

        #endregion
        #endregion
    }
}
