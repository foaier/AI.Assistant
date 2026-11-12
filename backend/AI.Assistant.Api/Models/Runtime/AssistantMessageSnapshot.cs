#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 可传输的消息快照，包含角色、内容和生成状态。
/// </summary>
public sealed record AssistantMessageSnapshot(
    string Id,
    string ConversationId,
    string TurnId,
    string? RunId,
    AssistantMessageRole Role,
    AssistantMessageStatus Status,
    string ContentJson,
    DateTime? CompletedAt,
    string? ClientMessageId = null,
    AssistantTurnStatus? TurnStatus = null);
