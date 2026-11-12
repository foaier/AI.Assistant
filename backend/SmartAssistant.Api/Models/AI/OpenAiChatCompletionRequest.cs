#nullable enable
using System.Text.Json.Serialization;

namespace SmartAssistant.Models.AI;

/// <summary>
/// 封装打开聊天补全请求所需字段，供模型供应商协议序列化和反序列化使用。
/// </summary>
internal sealed class OpenAiChatCompletionRequest
{
    /// <summary>
    /// 记录当前选择的模型标识。
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 保存发送给模型供应商的消息请求字段。
    /// </summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<OpenAiMessage> Messages { get; init; } = Array.Empty<OpenAiMessage>();

    /// <summary>
    /// 控制模型生成文本的随机程度，数值越高输出变化越明显。
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>
    /// 限制模型从累计概率范围内选择候选词的采样阈值。
    /// </summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    /// <summary>
    /// 限制模型生成内容的令牌数量。
    /// </summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 保存发送给模型供应商的流式输出请求字段。
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>
    /// 保存发送给模型供应商的流式输出选项请求字段。
    /// </summary>
    [JsonPropertyName("stream_options")]
    public OpenAiStreamOptions? StreamOptions { get; init; }

    /// <summary>
    /// 保存发送给模型供应商的工具请求字段。
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<OpenAiTool>? Tools { get; init; }

    /// <summary>
    /// 保存本次模型请求的工具选择策略。
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }

    /// <summary>
    /// 控制模型是否允许并行生成工具调用。
    /// </summary>
    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; init; }
}
