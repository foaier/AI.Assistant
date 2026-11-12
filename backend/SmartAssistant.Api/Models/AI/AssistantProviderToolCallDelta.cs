#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 工具调用参数的流式增量，需按调用标识合并后才能执行。
/// </summary>
public sealed record AssistantProviderToolCallDelta(
    int Index,
    string? ToolCallId,
    string? Name,
    string? ArgumentsDelta);
