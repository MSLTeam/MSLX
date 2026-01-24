using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    /// <summary>
    /// HTTP响应包装类
    /// </summary>
    public class HttpResponse
    {
        
        public HttpStatusCode StatusCode { get; set; }
        public string Content { get; set; }
        public Exception? Exception { get; set; } = null;
        public bool IsSuccess => StatusCode == HttpStatusCode.OK && Exception == null;

        public HttpResponse()
        {
            StatusCode = default;
            Content = string.Empty;
            Exception = null;
        }
    }

    /// <summary>
    /// HTTP服务类 - 提供统一的HTTP请求功能
    /// </summary>
    public class HttpService : IDisposable
    {
        // private static readonly HttpClient _HttpClient = new HttpClient();
        private bool _disposed = false;

        /// <summary>
        /// POST请求内容类型枚举
        /// </summary>
        public enum PostContentType
        {
            Json,
            Text,
            FormUrlEncoded,
            None
        }

        #region GET 请求方法

        /// <summary>
        /// 异步获取HTTP内容
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="configureHeaders">自定义请求头配置</param>
        /// <param name="uaType">User-Agent类型</param>
        /// <param name="customUA">自定义User-Agent（当uaType为Custom时使用）</param>
        /// <returns>响应内容字符串</returns>
        public static async Task<string> GetContentAsync(
            string url,
            object? queryParameters = null,
            Action<HttpRequestHeaders>? configureHeaders = null,
            UAManager.UAType uaType = UAManager.UAType.MSLX,
            string? customUA = null)
        {
            var response = await GetAsync(url, queryParameters, configureHeaders, uaType, customUA);

            if (!response.IsSuccess)
            {
                throw new HttpRequestException(
                    $"HTTP请求失败: {response.Content}",
                    new Exception(response.StatusCode.ToString()));
            }

            return response.Content;
        }

        /// <summary>
        /// 异步执行HTTP GET请求
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="queryParameters">查询参数</param>
        /// <param name="configureHeaders">自定义请求头配置</param>
        /// <param name="uaType">User-Agent类型</param>
        /// <param name="customUA">自定义User-Agent</param>
        /// <returns>HTTP响应对象</returns>
        public static async Task<HttpResponse> GetAsync(
            string url,
            object? queryParameters = null,
            Action<HttpRequestHeaders>? configureHeaders = null,
            UAManager.UAType uaType = UAManager.UAType.MSLX,
            string? customUA = null)
        {
            using var httpClient = new HttpClient();
            var httpResponse = new HttpResponse();

            try
            {
                // 配置User-Agent
                ConfigureUserAgent(httpClient, uaType, customUA);

                // 拼接查询参数
                if (queryParameters != null)
                {
                    url = AppendQueryParameters(url, queryParameters);
                }

                // 应用自定义请求头
                configureHeaders?.Invoke(httpClient.DefaultRequestHeaders);

                Console.WriteLine($"HTTP GET: {url}");
                // LogHelper.Write.Info($"HTTP GET: {url}");

                var response = await httpClient.GetAsync(url);
                httpResponse.StatusCode = response.StatusCode;
                httpResponse.Content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"HTTP GET 返回非成功状态码: {response.StatusCode} - {url}");
                    // LogHelper.Write.Warning($"HTTP GET 返回非成功状态码: {response.StatusCode} - {url}");
                }
            }
            catch (Exception ex)
            {
                httpResponse.Exception = ex;
                Console.WriteLine($"HTTP GET异常: {ex.Message} - URL: {url}");
                // LogHelper.Write.Error($"HTTP GET异常: {ex.Message} - URL: {url}");
            }
            httpClient.Dispose();
            return httpResponse;
        }

        #endregion

        #region POST 请求方法

        /// <summary>
        /// 异步执行HTTP POST请求
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="contentType">内容类型</param>
        /// <param name="parameterData">POST参数数据</param>
        /// <param name="configureHeaders">自定义请求头配置</param>
        /// <param name="uaType">User-Agent类型</param>
        /// <param name="customUA">自定义User-Agent</param>
        /// <returns>HTTP响应对象</returns>
        public static async Task<HttpResponse> PostAsync(
            string url,
            object? queryParameters = null,
            PostContentType contentType = PostContentType.Json,
            object? parameterData = null,
            Action<HttpRequestHeaders>? configureHeaders = null,
            UAManager.UAType uaType = UAManager.UAType.MSLX,
            string? customUA = null)
        {
            using var httpClient = new HttpClient();
            var httpResponse = new HttpResponse();

            try
            {
                // 配置User-Agent
                ConfigureUserAgent(httpClient, uaType, customUA);

                if (queryParameters != null)
                {
                    url = AppendQueryParameters(url, queryParameters);
                }

                // 创建请求内容
                var content = CreateHttpContent(contentType, parameterData, httpClient);

                // 应用自定义请求头
                configureHeaders?.Invoke(httpClient.DefaultRequestHeaders);

                // LogHelper.Write.Info($"HTTP POST: {url}");
                Console.WriteLine($"HTTP POST: {url}");

                var response = await httpClient.PostAsync(url, content);
                httpResponse.StatusCode = response.StatusCode;
                httpResponse.Content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // LogHelper.Write.Warning($"HTTP POST 返回非成功状态码: {response.StatusCode} - {url}");
                    Console.WriteLine($"HTTP POST 返回非成功状态码: {response.StatusCode} - {url}");
                }
            }
            catch (Exception ex)
            {
                httpResponse.Exception = ex;
                // LogHelper.Write.Error($"HTTP POST异常: {ex.Message} - URL: {url}");
                Console.WriteLine($"HTTP POST异常: {ex.Message} - URL: {url}");
            }

            httpClient.Dispose();
            return httpResponse;
        }

        #endregion

        #region 私有辅助方法

        /// <summary>
        /// 配置HttpClient的User-Agent
        /// </summary>
        private static void ConfigureUserAgent(
            HttpClient httpClient,
            UAManager.UAType uaType,
            string? customUA)
        {
            string userAgent;
            if (uaType== UAManager.UAType.Custom)
            {
                userAgent = string.IsNullOrEmpty(customUA)
                ? UAManager.GetUA(uaType)
                : customUA;
            }
            else
            {
                userAgent = UAManager.GetUA(uaType);
            }
            if (!string.IsNullOrEmpty(userAgent))
            {
                httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(userAgent);
            }
        }

        /// <summary>
        /// 根据内容类型创建HttpContent
        /// </summary>
        private static HttpContent? CreateHttpContent(
            PostContentType contentType,
            object? parameterData,
            HttpClient httpClient)
        {
            switch (contentType)
            {
                case PostContentType.Json:
                    httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/json"));
                    return new StringContent(
                        JsonSerializer.Serialize(parameterData),
                        Encoding.UTF8,
                        "application/json");

                case PostContentType.Text:
                    httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("text/plain"));
                    return new StringContent(
                        parameterData?.ToString() ?? string.Empty,
                        Encoding.UTF8,
                        "text/plain");

                case PostContentType.FormUrlEncoded:
                    httpClient.DefaultRequestHeaders.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                    return CreateFormUrlEncodedContent(parameterData);

                case PostContentType.None:
                    return null;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(contentType),
                        contentType,
                        "不支持的内容类型");
            }
        }

        /// <summary>
        /// 创建FormUrlEncoded内容
        /// </summary>
        private static FormUrlEncodedContent CreateFormUrlEncodedContent(object? parameterData)
        {
            if (parameterData == null)
                return new FormUrlEncodedContent(new Dictionary<string, string>());

            if (parameterData is IDictionary<string, string> dictionary)
            {
                return new FormUrlEncodedContent(dictionary);
            }

            // 解析字符串格式的参数 (key1=value1&key2=value2)
            var paramStr = parameterData?.ToString() ?? string.Empty;
            var keyValuePairs = paramStr
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            return new FormUrlEncodedContent(keyValuePairs);
        }

        /// <summary>
        /// 拼接查询参数到URL
        /// </summary>
        private static string AppendQueryParameters(string url, object? queryParameters)
        {
            if (queryParameters == null)
                return url;

            var queryDict = ParseQueryParameters(queryParameters);
            if (queryDict.Count == 0)
                return url;

            var queryString = string.Join("&",
                queryDict.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            // 判断URL是否已包含查询参数
            var separator = url.Contains('?') ? "&" : "?";
            return $"{url}{separator}{queryString}";
        }

        /// <summary>
        /// 解析查询参数为字典
        /// </summary>
        private static Dictionary<string, string> ParseQueryParameters(object? queryParameters)
        {
            if (queryParameters == null)
                return new Dictionary<string, string>();

            if (queryParameters is IDictionary<string, string> dictionary)
            {
                return new Dictionary<string, string>(dictionary);
            }

            // 解析字符串格式的参数 (key1=value1&key2=value2)
            var paramStr = queryParameters?.ToString() ?? string.Empty;
            return paramStr
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);
        }

        #endregion

        #region IDisposable 实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// User-Agent管理器
    /// </summary>
    public static class UAManager
    {
        /// <summary>
        /// User-Agent类型枚举
        /// </summary>
        public enum UAType
        {
            MSLX,
            Win,
            Linux,
            Mac,
            Android,
            iOS,
            Custom
        }

        private static readonly string _mslxUA =
            $"MSLTeam-MSLX/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";

        private static readonly string _winUA =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly string _linuxUA =
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly string _macUA =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly string _androidUA =
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.6778.200 Mobile Safari/537.36";

        private static readonly string _iosUA =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        /// <summary>
        /// 获取指定类型的User-Agent字符串
        /// </summary>
        /// <param name="type">User-Agent类型</param>
        /// <returns>User-Agent字符串</returns>
        public static string GetUA(UAType type = UAType.MSLX)
        {
            return type switch
            {
                UAType.MSLX => _mslxUA,
                UAType.Win => _winUA,
                UAType.Linux => _linuxUA,
                UAType.Mac => _macUA,
                UAType.Android => _androidUA,
                UAType.iOS => _iosUA,
                UAType.Custom => string.Empty,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的UA类型")
            };
        }

        /// <summary>
        /// 根据当前操作系统自动选择合适的User-Agent
        /// </summary>
        /// <returns>User-Agent字符串</returns>
        public static string GetPlatformUA()
        {
            if (OperatingSystem.IsWindows())
                return _winUA;
            if (OperatingSystem.IsLinux())
                return _linuxUA;
            if (OperatingSystem.IsMacOS())
                return _macUA;
            if (OperatingSystem.IsAndroid())
                return _androidUA;
            if (OperatingSystem.IsIOS())
                return _iosUA;

            return _winUA; // 默认返回Windows UA
        }
    }
}
