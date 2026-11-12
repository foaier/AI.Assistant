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
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantRunEventSequence() { }

    /// <summary>
    /// 关联产生此事件、消息或工具调用的执行运行。
    /// </summary>
    public string RunId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    public string TurnId { get; private set; } = string.Empty;
    /// <summary>
    /// 记录事件流当前的序号上限。
    /// </summary>
    public long Head { get; private set; }
    /// <summary>
    /// 标记事件流是否已经保存终态事件。
    /// </summary>
    public bool HasTerminalEvent { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手执行事件序号领域对象，并初始化其状态和时间字段。
    /// </summary>
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

    /// <summary>
    /// 从持久化数据还原助手执行事件序号的标识、访问范围和生命周期状态。
    /// </summary>
    public void Rehydrate(long head, bool hasTerminalEvent)
    {
        if (head < Head)
            throw new BizResultException(BusinessCodes.Conflict, "sequence_head_cannot_move_backwards");
        Head = head;
        HasTerminalEvent = hasTerminalEvent;
    }

    /// <summary>
    /// 判断事件类型或运行状态是否已经进入不可逆终态。
    /// </summary>
    private static bool IsTerminal(string type)
        => string.Equals(type, AssistantRunEventTypes.RunCompleted, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunFailed, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunCancelled, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunExpired, StringComparison.Ordinal);

    /// <summary>
    /// 判断事件类型是否属于当前支持的公开事件集合。
    /// </summary>
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
