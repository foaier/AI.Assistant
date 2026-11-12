#nullable enable
using System;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 为同一运行连续生成事件序号，并在记录终态事件后拒绝继续追加。
/// </summary>
public sealed class AssistantRunEventSequence
{
    private AssistantRunEventSequence() { }

    public string RunId { get; private set; } = string.Empty;
    public string TurnId { get; private set; } = string.Empty;
    public long Head { get; private set; }
    public bool HasTerminalEvent { get; private set; }

    public static AssistantRunEventSequence Create(string runId, string turnId, long head = 0)
    {
        if (head < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "sequence_head_invalid");

        return new AssistantRunEventSequence
        {
            RunId = AssistantRuntimeGuard.Required(runId, "RunId"),
            TurnId = AssistantRuntimeGuard.Required(turnId, "TurnId"),
            Head = head
        };
    }

    /// <summary>
    /// 校验事件类型后递增序号并创建事件信封；已记录终态事件时拒绝追加。
    /// </summary>
    public AssistantRunEventEnvelope Append<TPayload>(
        string eventId,
        string type,
        DateTime occurredAt,
        TPayload payload)
    {
        if (HasTerminalEvent)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_terminal_event_already_set");
        if (!IsKnownEventType(type))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_run_event_type_invalid");

        if (Head == long.MaxValue)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_sequence_exhausted");

        var nextSequence = Head + 1;
        var envelope = AssistantRunEventEnvelope.Create(
            eventId,
            RunId,
            TurnId,
            nextSequence,
            type,
            occurredAt,
            payload);
        Head = nextSequence;
        if (IsTerminal(type))
            HasTerminalEvent = true;
        return envelope;
    }

    public void Rehydrate(long head, bool hasTerminalEvent)
    {
        if (head < Head)
            throw new BizResultException(BusinessCodes.Conflict, "sequence_head_cannot_move_backwards");
        Head = head;
        HasTerminalEvent = hasTerminalEvent;
    }

    private static bool IsTerminal(string type)
        => string.Equals(type, AssistantRunEventTypes.RunCompleted, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunFailed, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunCancelled, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunExpired, StringComparison.Ordinal);

    private static bool IsKnownEventType(string type)
        => !string.IsNullOrWhiteSpace(type)
            && (string.Equals(type, AssistantRunEventTypes.RunAccepted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.RunStarted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.MessageDelta, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.MessageCompleted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.ToolCallStarted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.ToolCallCompleted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.RunCompleted, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.RunFailed, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.RunCancelled, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.RunExpired, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.Heartbeat, StringComparison.Ordinal)
                || string.Equals(type, AssistantRunEventTypes.ReplayGap, StringComparison.Ordinal));
}
