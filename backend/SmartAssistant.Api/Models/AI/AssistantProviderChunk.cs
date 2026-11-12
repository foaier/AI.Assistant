#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 模型适配器返回的统一流式片段。
/// </summary>
public sealed record AssistantProviderChunk(
    AssistantProviderChunkKind Kind,
    string? TextDelta = null,
    AssistantProviderToolCallDelta? ToolCall = null,
    string? FinishReason = null,
    AssistantProviderUsage? Usage = null,
    AssistantProviderError? Error = null,
    string? ProviderRequestId = null);
