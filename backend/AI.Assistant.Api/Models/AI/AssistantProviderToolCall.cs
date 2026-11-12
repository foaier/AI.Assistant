#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 模型消息中的工具调用请求及其参数。
/// </summary>
public sealed record AssistantProviderToolCall(
    string ToolCallId,
    string Name,
    string ArgumentsJson);
