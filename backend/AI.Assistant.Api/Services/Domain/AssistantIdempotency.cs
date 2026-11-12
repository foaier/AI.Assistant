#nullable enable
using AI.Assistant.Models.Runtime;
using AI.Assistant.Platform;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 比较客户端消息标识对应的请求内容，决定复用已有轮次还是报告冲突。
/// </summary>
public static class AssistantIdempotency
{
    /// <summary>
    /// 比较已有轮次的请求指纹，内容一致时允许重放，不一致时报告幂等冲突。
    /// </summary>
    public static AssistantIdempotencyResult Evaluate(
        AssistantTurn existingTurn,
        string messageType,
        string text,
        string locale)
    {
        ArgumentNullException.ThrowIfNull(existingTurn);
        var requestHash = AssistantRuntimeGuard.ComputeRequestHash(messageType, text, locale);
        if (string.Equals(existingTurn.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AssistantIdempotencyResult(
                AssistantIdempotencyOutcome.Replayed,
                existingTurn.Id,
                requestHash);
        }

        throw new BizResultException(
            BusinessCodes.Conflict,
            "assistant_turn_idempotency_payload_conflict");
    }
}
