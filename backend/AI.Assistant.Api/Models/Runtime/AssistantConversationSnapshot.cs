#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 对话的只读状态快照，供运行服务和客户端传递对话信息。
/// </summary>
public sealed record AssistantConversationSnapshot(
    string Id,
    string AssistantId,
    string SessionId,
    string PublishedVersionId,
    string Locale,
    AssistantExecutionMode ExecutionMode,
    AssistantConversationStatus Status,
    long LastTurnNo,
    DateTime LastActivityAt);
