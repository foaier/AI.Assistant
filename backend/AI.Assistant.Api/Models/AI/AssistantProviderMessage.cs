#nullable enable
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.AI;

/// <summary>
/// 发送给模型供应商的消息，包含角色、正文及工具调用上下文。
/// </summary>
public sealed record AssistantProviderMessage(
    AssistantMessageRole Role,
    string Content,
    string? Name = null,
    string? ToolCallId = null,
    IReadOnlyList<AssistantProviderToolCall>? ToolCalls = null);
