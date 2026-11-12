#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 封装打开函数所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiFunction
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 记录此资源的可选说明。
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 保存函数可接受参数的 JSON Schema。
    /// </summary>
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}
