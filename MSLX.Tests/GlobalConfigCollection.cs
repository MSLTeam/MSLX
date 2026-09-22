namespace MSLX.Tests;

/// <summary>
/// 全局静态配置串行集合：凡是读写 IConfigBase 静态状态的测试类都必须加入此集合，
/// 防止未来新增的配置相关测试类与本类并行执行时互相污染静态配置。
/// </summary>
[CollectionDefinition("GlobalConfig")]
public class GlobalConfigCollection;
