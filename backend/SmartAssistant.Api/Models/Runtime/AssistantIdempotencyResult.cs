#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 领域层的幂等判定结果。
/// </summary>
public sealed record AssistantIdempotencyResult(
    AssistantIdempotencyOutcome Outcome,
    string TurnId,
    string RequestHash);
