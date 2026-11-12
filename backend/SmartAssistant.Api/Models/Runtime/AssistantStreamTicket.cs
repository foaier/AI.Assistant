#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 绑定授权范围、运行和传输类型的短期流式订阅凭据。
/// </summary>
public sealed record AssistantStreamTicket(
    string Value,
    string AssistantId,
    string PublishedVersionId,
    string SessionId,
    string ConversationId,
    string TurnId,
    string RunId,
    AssistantExecutionMode ExecutionMode,
    IReadOnlySet<AssistantTransportKind> AllowedTransports,
    DateTime ExpiresAt);
