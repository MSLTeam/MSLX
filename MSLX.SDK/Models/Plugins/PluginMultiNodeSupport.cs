using Newtonsoft.Json;

namespace MSLX.SDK.Models.Plugins;

[JsonConverter(typeof(PluginMultiNodeSupportConverter))]
public enum PluginMultiNodeSupport { NoTest = 0, Supported = 1, Unsupported = 2 }

public sealed class PluginMultiNodeSupportConverter : JsonConverter<PluginMultiNodeSupport>
{
    public override void WriteJson(JsonWriter writer, PluginMultiNodeSupport value, JsonSerializer serializer) =>
        writer.WriteValue(value switch
        {
            PluginMultiNodeSupport.NoTest => "no-test",
            PluginMultiNodeSupport.Supported => "true",
            PluginMultiNodeSupport.Unsupported => "false",
            _ => throw new JsonSerializationException("插件多节点支持声明无效")
        });

    public override PluginMultiNodeSupport ReadJson(JsonReader reader, Type objectType, PluginMultiNodeSupport existingValue, bool hasExistingValue, JsonSerializer serializer) =>
        reader.TokenType == JsonToken.String ? reader.Value switch
        {
            "no-test" => PluginMultiNodeSupport.NoTest,
            "true" => PluginMultiNodeSupport.Supported,
            "false" => PluginMultiNodeSupport.Unsupported,
            _ => throw new JsonSerializationException("插件多节点支持声明只能为 true、false 或 no-test 字符串")
        } : throw new JsonSerializationException("插件多节点支持声明必须为字符串枚举");
}
