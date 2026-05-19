namespace MSLX.SDK.Interfaces;

public enum PluginHttpContentType { Json, FormUrlEncoded, Text, Octet }

public class PluginHttpResponse
{
    public string? Content { get; set; }
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> Cookies { get; set; } = new();
    public bool IsSuccessStatusCode { get; set; }
    public Exception? ResponseException { get; set; }
}

public interface IMSLXHttp
{
    Task<PluginHttpResponse> GetAsync(
        string url, 
        Dictionary<string, string>? queryParameters = null, 
        Dictionary<string, string>? headers = null, 
        TimeSpan? timeout = null);

    Task<PluginHttpResponse> PostAsync(
        string url, 
        PluginHttpContentType contentType, 
        object data, 
        Dictionary<string, string>? headers = null, 
        TimeSpan? timeout = null);
}