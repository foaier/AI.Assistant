#nullable enable
using System.Text.Json.Serialization;

namespace AI.Assistant.Models.AI;

/// <summary>
/// 封装打开消息所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiMessage
{
    /// <summary>
    /// 区分用户、助手和工具消息。
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// 表示打开消息的内容信息。
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 记录此资源的展示名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// 关联模型发起的具体工具调用。
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    /// <summary>
    /// 表示打开消息的工具调用信息。
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; init; }
}
