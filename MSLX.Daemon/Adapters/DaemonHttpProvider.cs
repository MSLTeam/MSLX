using MSLX.SDK.Interfaces;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Adapters;

public class DaemonHttpProvider : IMSLXHttp
{
    public async Task<PluginHttpResponse> GetAsync(
        string url, 
        Dictionary<string, string>? queryParameters = null, 
        Dictionary<string, string>? headers = null, 
        TimeSpan? timeout = null)
    {
        var response = await GeneralApi.GetAsync(url, queryParameters, headers, timeout);
        return MapResult(response);
    }

    public async Task<PluginHttpResponse> PostAsync(
        string url, 
        PluginHttpContentType contentType, 
        object data, 
        Dictionary<string, string>? headers = null, 
        TimeSpan? timeout = null)
    {
        var daemonContentType = (HttpService.PostContentType)(int)contentType;
        
        var response = await GeneralApi.PostAsync(url, daemonContentType, data, headers, timeout);
        return MapResult(response);
    }
    
    private PluginHttpResponse MapResult(HttpService.HttpResponse res)
    {
        return new PluginHttpResponse
        {
            Content = res.Content,
            StatusCode = res.StatusCode,
            Headers = res.Headers,
            Cookies = res.Cookies,
            IsSuccessStatusCode = res.IsSuccessStatusCode,
            ResponseException = res.ResponseException as Exception
        };
    }
}