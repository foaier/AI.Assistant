#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 已合并完毕的模型工具调用，包含完整的调用标识和参数。
/// </summary>
public sealed record AssistantAssembledToolCall(
    int Index,
    string ToolCallId,
    string Name,
    string ArgumentsJson);
