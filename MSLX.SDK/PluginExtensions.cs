using Microsoft.AspNetCore.Builder;
using MSLX.SDK.Attributes;

namespace MSLX.SDK;

public static class PluginExtensions
{
    public static Interfaces.IPluginConfigBridge Config(this IPlugin plugin)
    {
        return MSLX.Config.GetPluginConfig(plugin.Id);
    }

    /// <summary>
    /// 为 Minimal API 路由端点声明所允许的降权 Token 权限范围（Scope）
    /// </summary>
    public static TBuilder AllowTokenScope<TBuilder>(this TBuilder builder, params string[] allowedScopes)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new AllowTokenScopeAttribute(allowedScopes));
        });
        return builder;
    }
}