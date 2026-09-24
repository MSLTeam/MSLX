using System;
using System.Net;
using System.Net.Sockets;

namespace MSLX.SDK.Utils;

public static class UrlSecurityGuard
{
    // 提供给上层应用注入全局安全配置的委托
    public static Func<bool> IsSsrfProtectionEnabled { get; set; } = () => true;

    public static string? CheckUrlSafety(string urlStr)
    {
        if (!IsSsrfProtectionEnabled()) return null; // 放行

        if (!Uri.TryCreate(urlStr, UriKind.Absolute, out Uri? uri))
        {
            return "无效的 URL 格式";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "只允许 http 和 https 协议";
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            foreach (var ip in addresses)
            {
                var checkResult = CheckIpStatus(ip);
                if (checkResult != null) return checkResult;
            }
        }
        catch (Exception)
        {
            return "无法解析域名或地址无效";
        }

        return null;
    }

    private static string? CheckIpStatus(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return "非法下载地址，禁止请求内部网络或云服务元数据接口";

        byte[] bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes.Length != 4) return null;

            if (bytes[0] == 0 || bytes[0] == 10 || 
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || 
               (bytes[0] == 192 && bytes[1] == 168) || 
               (bytes[0] == 169 && bytes[1] == 254) || 
               (bytes[0] == 100 && (bytes[1] & 0b11000000) == 64))
            {
                return "非法下载地址，禁止请求内部网络或云服务元数据接口";
            }
            
            // Fake-IP: 198.18.0.0/15
            if (bytes[0] == 198 && (bytes[1] & 0xfe) == 18)
            {
                return "触发 SSRF 安全拦截，疑似处于代理环境。如必须使用代理下载，请在主控设置中酌情关闭“离线下载 SSRF 保护”。";
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return "非法下载地址，禁止请求内部网络或云服务元数据接口";
            
            // ULA (fc00::/7) Often used by Fake-IP IPv6
            if (bytes.Length >= 1 && (bytes[0] & 0xfe) == 0xfc)
            {
                return "触发 SSRF 安全拦截，疑似处于代理环境。如必须使用代理下载，请在主控设置中酌情关闭“离线下载 SSRF 保护”。";
            }
        }

        return null;
    }
}
