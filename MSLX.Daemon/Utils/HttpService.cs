using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using static MSLX.Daemon.Utils.HttpService;

namespace MSLX.Daemon.Utils;
    // MSLAPI3
    public class MSLApi
    {
        public static string ApiUrl { get; } = "https://api.mslmc.cn/v3";

        public async static Task<(bool Success, object? Data, string? Msg)> GetDataAsync(string path, string dataKey = "data", Dictionary<string, string>? queryParameters = null)
        {
            var getResponse = await GetAsync(path, queryParameters);
            if (getResponse.IsSuccessStatusCode)
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

        public async static Task<HttpService.HttpResponse> GetAsync(string path, Dictionary<string, string>? queryParameters)
        {
            using var service = new HttpService();
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));
            string url = ApiUrl + path;
            if (queryParameters != null && queryParameters.Count > 0)
            {
                string queryString = string.Join("&", queryParameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                url = $"{url}?{queryString}";
            }
            var getRequest = new HttpService.HttpRequest
            {
                Url = url,
                Method = HttpMethod.Get,
                Headers = new Dictionary<string, string>
                {
                    ["deviceID"] = PlatFormServices.GetDeviceId()
                }
            };
            var getResponse = await service.SendAsync(getRequest);
            service.Dispose();
            return getResponse;
        }

        public async static Task<HttpService.HttpResponse> PostAsync(string path, PostContentType postContentType, object data)
        {
            using var service = new HttpService();
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));
            var postRequest = new HttpService.HttpRequest
            {
                Url = ApiUrl + path,
                Method = HttpMethod.Post,
                Headers = new Dictionary<string, string>
                {
                    ["deviceID"] = PlatFormServices.GetDeviceId()
                }
            };
            postRequest.ContentType = postContentType;
            postRequest.Data = data;
            var postResponse = await service.SendAsync(postRequest);
            service.Dispose();
            return postResponse;
        }
    }

    // MSL用户中心API
    public class MSLUser
    {
        public static string ApiUrl { get; } = "https://user.mslmc.net/api";
        
        public async static Task<HttpService.HttpResponse> GetAsync(string path, Dictionary<string, string>? queryParameters, Dictionary<string, string>? headers = null)
        {
            using var service = new HttpService();
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));
            string url = ApiUrl + path;
            if (queryParameters != null && queryParameters.Count > 0)
            {
                string queryString = string.Join("&", queryParameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                url = $"{url}?{queryString}";
            }
            var getRequest = new HttpService.HttpRequest
            {
                Url = url,
                Method = HttpMethod.Get,
            };
            if (headers != null)
            {
                getRequest.Headers = headers;
            }
            var getResponse = await service.SendAsync(getRequest);
            service.Dispose();
            return getResponse;
        }

        public async static Task<HttpService.HttpResponse> PostAsync(string path, PostContentType postContentType, object data, Dictionary<string, string>? headers = null)
        {
            using var service = new HttpService();
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));
            var postRequest = new HttpService.HttpRequest
            {
                Url = ApiUrl + path,
                Method = HttpMethod.Post
            };
            postRequest.ContentType = postContentType;
            postRequest.Data = data;
            if (headers != null)
            {
                postRequest.Headers = headers;
            }
            var postResponse = await service.SendAsync(postRequest);
            service.Dispose();
            return postResponse;
        }
    }

    // 通用API
    public class GeneralApi
    {
        /// <summary>
        /// 通用 Get 请求
        /// </summary>
        public async static Task<HttpService.HttpResponse> GetAsync(
            string url, 
            Dictionary<string, string>? queryParameters = null, 
            Dictionary<string, string>? headers = null,
            TimeSpan? timeout = null)
        {
            using var service = new HttpService(timeout: timeout);
            
            // 默认设置 UA，防止被拦截
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));

            // 智能拼接 Query 参数
            if (queryParameters != null && queryParameters.Count > 0)
            {
                string separator = url.Contains("?") ? "&" : "?";
                string queryString = string.Join("&", queryParameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));
                url = $"{url}{separator}{queryString}";
            }

            var getRequest = new HttpService.HttpRequest
            {
                Url = url,
                Method = HttpMethod.Get
            };

            if (headers != null)
            {
                getRequest.Headers = headers;
            }

            return await service.SendAsync(getRequest);
        }

        /// <summary>
        /// 通用 Post 请求
        /// </summary>
        public async static Task<HttpService.HttpResponse> PostAsync(
            string url, 
            PostContentType postContentType, 
            object data, 
            Dictionary<string, string>? headers = null,
            TimeSpan? timeout = null)
        {
            using var service = new HttpService(timeout: timeout);
            service.SetDefaultHeadersUA(UAManager.GetUA(UAManager.UAType.MSLX));

            var postRequest = new HttpService.HttpRequest
            {
                Url = url,
                Method = HttpMethod.Post,
                ContentType = postContentType,
                Data = data
            };

            if (headers != null)
            {
                postRequest.Headers = headers;
            }

            return await service.SendAsync(postRequest);
        }
    }

    // 底层封装
    public class HttpService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _httpHandler;
        private bool _disposed = false;

        public HttpService(TimeSpan? timeout = null, bool? allowAutoRedirect = null, bool? useCookies = null, DecompressionMethods? decompressionMethods = null)
        {
            _httpHandler = new HttpClientHandler();
            if (allowAutoRedirect != null)
            {
                _httpHandler.AllowAutoRedirect = allowAutoRedirect.Value;
            }
            if (useCookies != null)
            {
                _httpHandler.UseCookies = useCookies.Value;
                _httpHandler.CookieContainer = new CookieContainer();
            }
            if (decompressionMethods != null)
            {
                _httpHandler.AutomaticDecompression = decompressionMethods.Value;
            }

            _httpClient = new HttpClient(_httpHandler);
            if (timeout != null)
            {
                _httpClient.Timeout = timeout.Value;
            }
        }

        public enum PostContentType { Json, FormUrlEncoded, Text, Octet }
        public class HttpRequest
        {
            public required string Url { get; set; }
            public HttpMethod Method { get; set; } = HttpMethod.Get;
            public Dictionary<string, string> Headers { get; set; } = new();
            public object? Data { get; set; }
            public PostContentType ContentType { get; set; } = PostContentType.Json;
        }

        public class HttpResponse
        {
            public string? Content { get; set; }
            public int StatusCode { get; set; }
            public Dictionary<string, string> Headers { get; set; } = new();
            public Dictionary<string, string> Cookies { get; set; } = new();
            public bool IsSuccessStatusCode { get; set; }
            public object? ResponseException { get; set; }
        }

        public async Task<HttpResponse> SendAsync(HttpRequest request)
        {
            using var message = new HttpRequestMessage(request.Method, request.Url);

            foreach (var header in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Method != HttpMethod.Get && request.Data != null)
            {
                _httpClient.DefaultRequestHeaders.Accept.TryParseAdd(HttpAcceptType(request.ContentType));
                message.Content = CreateHttpContent(request.ContentType, request.Data);
            }

            try
            {
                using var response = await _httpClient.SendAsync(message);
                return await CreateHttpResponse(response);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                return new HttpResponse
                {
                    StatusCode = 0,
                    IsSuccessStatusCode = false,
                    ResponseException = new TimeoutException("Request timed out", ex)
                };
            }
            catch(Exception ex)
            {
                return new HttpResponse
                {
                    StatusCode = 0,
                    IsSuccessStatusCode = false,
                    ResponseException = ex
                };
            }
        }

        private string HttpAcceptType(PostContentType contentType)
        {
            return contentType switch
            {
                PostContentType.Json => "application/json",
                PostContentType.FormUrlEncoded => "application/x-www-form-urlencoded",
                PostContentType.Text => "text/plain",
                PostContentType.Octet => "application/octet-stream",
                _ => throw new NotSupportedException($"Content type {contentType} is not supported")
            };
        }

        private HttpContent CreateHttpContent(PostContentType contentType, object data)
        {
            return contentType switch
            {
                PostContentType.Json => new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json"),
                PostContentType.FormUrlEncoded => new FormUrlEncodedContent(data as Dictionary<string, string> ?? new Dictionary<string, string>()),
                PostContentType.Text => new StringContent(data as string ?? "", Encoding.UTF8, "text/plain"),
                PostContentType.Octet => new ByteArrayContent(data as byte[] ?? new byte[0]),
                _ => throw new NotSupportedException($"Content type {contentType} is not supported")
            };
        }

        private async Task<HttpResponse> CreateHttpResponse(HttpResponseMessage response)
        {
            var result = new HttpResponse
            {
                StatusCode = (int)response.StatusCode,
                IsSuccessStatusCode = response.IsSuccessStatusCode,
                Headers = response.Headers
                    .Concat(response.Content.Headers)
                    .ToDictionary(h => h.Key, h => string.Join("; ", h.Value))
            };

            if (_httpHandler.CookieContainer != null)
            {
                var cookies = _httpHandler.CookieContainer.GetCookies(new Uri(response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Authority) ?? string.Empty));
                result.Cookies = cookies.Cast<Cookie>()
                    .ToDictionary(c => c.Name, c => c.Value);
            }

            result.Content = await response.Content.ReadAsStringAsync();
            return result;
        }

        public void SetDefaultHeadersUA(string ua)
        {
            if (_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Remove("User-Agent");
            }

            // 管你呢！直接塞！咩规范啊？我唔知啊～
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
        }

        public void ClearCookies()
        {
            _httpHandler.CookieContainer = new CookieContainer();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _httpClient?.Dispose();
            _httpHandler?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // ==========================================
    // 5. UA 管理器 (保持不变)
    // ==========================================
    public class UAManager
    {
        public enum UAType { MSLX, Win, Linux, Mac }

        private static readonly string _mslxUA = "MSLTeam-MSLX(Daemon)/" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        private static readonly string _winUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36";
        private static readonly string _linuxUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36";
        private static readonly string _macUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36";

        public static string GetUA(UAType type = UAType.MSLX)
        {
            return type switch
            {
                UAType.MSLX => _mslxUA,
                UAType.Win => _winUA,
                UAType.Linux => _linuxUA,
                UAType.Mac => _macUA,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
