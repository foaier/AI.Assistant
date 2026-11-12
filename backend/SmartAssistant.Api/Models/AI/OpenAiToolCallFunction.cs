#nullable enable
using System.Text.Json.Serialization;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 封装打开工具调用函数所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiToolCallFunction
{
    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 保存模型生成的工具调用参数 JSON。
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = "{}";
}
