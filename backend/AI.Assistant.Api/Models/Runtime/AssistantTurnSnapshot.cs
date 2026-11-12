#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 用户消息轮次的只读状态与请求信息。
/// </summary>
public sealed record AssistantTurnSnapshot(
    string Id,
    string ConversationId,
    string SessionId,
    string AssistantId,
    string PublishedVersionId,
    string ClientMessageId,
    string RequestHash,
    long TurnNo,
    AssistantExecutionMode ExecutionMode,
    AssistantTurnStatus Status,
    DateTime? CompletedAt,
    string? FailureCode);
