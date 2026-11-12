#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 匿名会话的只读状态快照。
/// </summary>
public sealed record AnonymousAssistantSessionSnapshot(
    string Id,
    string AssistantId,
    string PublishedVersionId,
    string SubjectHash,
    string Locale,
    AssistantExecutionMode ExecutionMode,
    AssistantSessionStatus Status,
    DateTime LastSeenAt,
    DateTime ExpiresAt);
