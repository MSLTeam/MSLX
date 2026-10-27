using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.NodesControllers;
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json.Linq;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public class NodeLinkErrorsTests
{
    [Theory]
    [InlineData("https", "https://master.test", "http://slave.test", true)]
    [InlineData("http", "https://master.test", "http://slave.test", true)]
    [InlineData("https", "http://master.test", "http://slave.test", true)]
    [InlineData("https", "https://master.test", "https://slave.test", false)]
    [InlineData("http", "http://master.test", "http://slave.test", false)]
    [InlineData("http", "http://master.test", "https://slave.test", false)]
    public void NodeAddress_RejectsHttpChildForHttpsMaster(string scheme, string masterUrl, string nodeUrl, bool blocked)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        var controller = new MasterNodeController(NullLogger<MasterNodeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var error = (string?)typeof(MasterNodeController).GetMethod("ValidateNodeAddress", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [new LinkNodeRequest { MasterUrl = masterUrl, NodeUrl = nodeUrl }, null]);
        if (blocked) Assert.Contains("HTTPS 主节点不能连接 HTTP 子节点", error);
        else Assert.Null(error);
    }

    [Theory]
    [InlineData(404, "not found", "HTTP 404")]
    [InlineData(502, "<html>Bad Gateway</html>", "HTTP 502")]
    [InlineData(200, "<html>Another service</html>", "未收到有效的 MSLX 接口响应")]
    [InlineData(200, "{", "未收到有效的 MSLX 接口响应")]
    [InlineData(200, "{\"code\":999999999999999999999999}", "未收到有效的 MSLX 接口响应")]
    [InlineData(401, "{\"code\":401,\"message\":\"子节点 LinkKey 错误\"}", "HTTP 401）：子节点 LinkKey 错误")]
    [InlineData(200, "{\"code\":400,\"message\":\"当前并非子节点模式，拒绝链接请求\"}", "当前并非子节点模式")]
    public void LinkResponse_PreservesActualCause_InsteadOfReportingProtocolMismatch(int status, string content, string expected)
    {
        var error = ReadResponse(new HttpService.HttpResponse
        {
            StatusCode = status, IsSuccessStatusCode = status == 200, Content = content
        });
        Assert.Contains(expected, error);
        Assert.DoesNotContain("协议不兼容", error);
    }

    [Fact]
    public void NetworkFailures_ExplainPortTimeoutDnsAndTls()
    {
        foreach (var (exception, expected) in new (Exception, string)[]
        {
            (new HttpRequestException("Connection refused"), "端口是否填写正确"),
            (new TimeoutException(), "超时"),
            (new HttpRequestException(HttpRequestError.NameResolutionError, "DNS failed"), "无法解析子节点域名"),
            (new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS failed"), "HTTPS 连接失败")
        })
        {
            var error = ReadResponse(new HttpService.HttpResponse { ResponseException = exception });
            Assert.Contains(expected, error);
            Assert.DoesNotContain("协议不兼容", error);
        }
        Assert.Null(ReadResponse(new HttpService.HttpResponse
        {
            StatusCode = 200, IsSuccessStatusCode = true, Content = "{\"code\":200,\"data\":{\"protocolVersion\":1}}"
        }));
    }

    [Theory]
    [InlineData(200, "{\"code\":200,\"data\":{\"protocolVersion\":1,\"pairingId\":\"0123456789abcdef0123456789abcdef\"}}", null)]
    [InlineData(200, "{\"code\":200,\"data\":{\"protocolVersion\":1}}", "未确认本次配对")]
    [InlineData(200, "{\"code\":200,\"data\":{\"protocolVersion\":1,\"pairingId\":\"another-pairing\"}}", "未确认本次配对")]
    [InlineData(200, "<html>Wrong port</html>", "响应不是有效的 MSLX 接口数据")]
    [InlineData(404, "not found", "HTTP 404")]
    public async Task ReverseProbe_RequiresConfirmationFromCurrentMaster(int status, string content, string? expected)
    {
        using var client = new HttpClient(new ProbeHandler(request =>
        {
            Assert.Equal("master.test", request.RequestUri!.Host);
            Assert.Equal("/api/node/protocol", request.RequestUri.AbsolutePath);
            Assert.Equal("?pairingId=0123456789abcdef0123456789abcdef", request.RequestUri.Query);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(content) };
        }));
        var error = await Probe(client);
        if (expected == null) Assert.Null(error);
        else
        {
            Assert.Contains("子节点连接主节点通讯失败", error);
            Assert.Contains(expected, error);
        }
    }

    [Fact]
    public async Task ReverseProbe_NetworkFailureAndTimeout_ReturnActionableErrors()
    {
        using var refused = new HttpClient(new ProbeHandler(_ => throw new HttpRequestException("Connection refused")));
        Assert.Contains("localhost 指向子节点自身", await Probe(refused));
        using var timeout = new HttpClient(new ProbeHandler(_ => throw new TaskCanceledException()));
        Assert.Contains("连接超时", await Probe(timeout));
        Assert.Contains("有效 HTTP/HTTPS 地址", await Probe(refused, "not-a-url"));
    }

    [Fact]
    public void Protocol_OnlyConfirmsCurrentlyActivePairing_AndKeepsOrdinaryResponseCompatible()
    {
        var controller = new SlaveNodeController(NullLogger<SlaveNodeController>.Instance);
        Assert.IsType<OkObjectResult>(controller.Protocol());
        Assert.IsType<BadRequestObjectResult>(controller.Protocol("unknown"));
        var field = typeof(MasterNodeController).GetField("_activePairingId", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, "pending-pairing");
            var result = Assert.IsType<OkObjectResult>(controller.Protocol("pending-pairing"));
            var response = Assert.IsType<ApiResponse<object>>(result.Value);
            Assert.Equal("pending-pairing", JObject.FromObject(response.Data!)["pairingId"]!.Value<string>());
            Assert.IsType<BadRequestObjectResult>(controller.Protocol("another-pairing"));
        }
        finally { field.SetValue(null, previous); }
    }

    private static string? ReadResponse(HttpService.HttpResponse response)
    {
        var controller = new MasterNodeController(NullLogger<MasterNodeController>.Instance);
        return (string?)typeof(MasterNodeController).GetMethod("ReadLinkResponse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, [response, "请求子节点配对", null]);
    }

    private static Task<string?> Probe(HttpClient client, string url = "http://master.test") =>
        (Task<string?>)typeof(SlaveNodeController).GetMethod("CheckMasterConnectionAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [new AcceptLinkRequest { MasterUrl = url, PairingId = "0123456789abcdef0123456789abcdef" },
                client, TestContext.Current.CancellationToken])!;

    private sealed class ProbeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
