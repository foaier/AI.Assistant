#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 轮次提交的幂等判定及可复用的已有运行信息。
/// </summary>
public sealed record AssistantTurnIdempotencyResult(
    AssistantIdempotencyOutcome Outcome,
    string TurnId,
    string RunId,
    string RequestHash);
