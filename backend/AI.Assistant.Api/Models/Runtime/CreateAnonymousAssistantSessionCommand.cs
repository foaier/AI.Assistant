#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 创建公开助手匿名会话的输入及客户端上下文。
/// </summary>
public sealed record CreateAnonymousAssistantSessionCommand(
    string AssistantKey,
    string SubjectHash,
    string Locale,
    AssistantExecutionMode ExecutionMode = AssistantExecutionMode.Public);
