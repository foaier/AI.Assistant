#nullable enable
using System.Text.Json;

namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 统一运行事件信封，供不同传输协议共享类型、顺序和负载格式。
/// </summary>
public sealed record AssistantRunEventEnvelope(
    string EventId,
    string RunId,
    string TurnId,
    long Sequence,
    string Type,
    DateTime OccurredAt,
    JsonElement Payload)
{
    /// <summary>
    /// 根据校验后的输入创建助手执行事件信封领域对象，并初始化其状态和时间字段。
    /// </summary>
    public static AssistantRunEventEnvelope Create<TPayload>(
        string eventId,
        string runId,
        string turnId,
        long sequence,
        string type,
        DateTime occurredAt,
        TPayload payload)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Event id is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run id is required.", nameof(runId));
        if (string.IsNullOrWhiteSpace(turnId))
            throw new ArgumentException("Turn id is required.", nameof(turnId));
        if (sequence < 1)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must start at one.");
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Event type is required.", nameof(type));

        return new AssistantRunEventEnvelope(
            eventId.Trim(),
            runId.Trim(),
            turnId.Trim(),
            sequence,
            type.Trim(),
            occurredAt,
            JsonSerializer.SerializeToElement(payload));
    }
}
