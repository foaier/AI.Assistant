#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 一次助手执行的只读状态、模型信息和事件进度。
/// </summary>
public sealed record AssistantRunSnapshot(
    string Id,
    string TurnId,
    string ConversationId,
    string SessionId,
    string AssistantId,
    string PublishedVersionId,
    AssistantExecutionMode ExecutionMode,
    string ProviderProfileRef,
    string Model,
    AssistantRunStatus Status,
    long SequenceHead,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureCode,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
