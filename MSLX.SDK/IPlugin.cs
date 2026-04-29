// MSLX.SDK/IPlugin.cs
namespace MSLX.SDK
{
    public interface IPlugin
    {
        // 插件唯一标识 仅支持英文
        string Id { get; } 
        
        // 插件名称
        string Name { get; }
        
        // 插件描述
        string Description { get; }
        
        // 插件版本
        string Version { get; }
        
        // 主项目最低版本
        string MinLoaderVersion { get; }

        // 开发者名称
        string Developer { get; }

        // 开发者主页
        string AuthorUrl { get; }

        // 插件项目主页
        string PluginUrl { get; }
    }
}