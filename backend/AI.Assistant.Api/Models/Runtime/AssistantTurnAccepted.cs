#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 轮次受理结果，返回运行标识、事件序号及可用于订阅的流票据。
/// </summary>
public sealed record AssistantTurnAccepted(
    string TurnId,
    string RunId,
    string ConversationId,
    string PublishedVersionId,
    string StreamTicket,
    long AcceptedSequence,
    DateTime ExpiresAt);
