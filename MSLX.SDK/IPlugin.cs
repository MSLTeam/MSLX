namespace MSLX.SDK
{
    public interface IPlugin
    {
        // 插件唯一标识 仅支持英文
        string Id { get; }

        // 插件名称
        string Name => "未命名插件";

        // 插件描述
        string Description => "这个开发者很懒，什么都没写。";

        // 插件图标 (应该打包在前端资源中)
        string Icon => "https://www.mslmc.cn/logo.png";

        // 插件版本
        string Version => "1.0.0";

        // 主项目最低版本
        string MinSDKVersion => "1.3.8.1";

        // 开发者名称
        string Developer => "不知道哇！";

        // 开发者主页
        string AuthorUrl => "https://github.com/MSLTeam";

        // 插件项目主页
        string PluginUrl => "https://github.com/MSLTeam";

        // 初始化 & 结束加载方法
        void OnLoad() { }
        void OnUnload() { }
    }
}