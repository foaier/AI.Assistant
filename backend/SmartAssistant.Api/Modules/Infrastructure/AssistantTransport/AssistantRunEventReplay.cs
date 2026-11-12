#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AssistantTransport;

/// <summary>
/// 维护有限长度的运行事件回放窗口，并检测顺序、重复和终态约束。
/// 仅负责回放语义，不直接访问网络或缓存服务。
/// </summary>
public sealed class AssistantRunEventReplay
{
    private readonly List<AssistantRunEventEnvelope> _events = new();
    private readonly string _runId;
    private readonly string _turnId;
    private long _sequenceHead;
    private bool _terminal;
    private AssistantRunStatus _persistedStatus;

    public AssistantRunEventReplay(
        string runId,
        string turnId,
        AssistantRunStatus persistedStatus = AssistantRunStatus.Accepted,
        long initialSequence = 0,
        int maxEvents = 256)
    {
        if (initialSequence < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "sequence_head_invalid");
        if (maxEvents < 1)
            throw new BizResultException(BusinessCodes.BusinessException, "replay_window_invalid");
        _runId = Required(runId, "RunId");
        _turnId = Required(turnId, "TurnId");
        _sequenceHead = initialSequence;
        _persistedStatus = persistedStatus;
        MaxEvents = maxEvents;
    }

    public long CurrentSequence => _sequenceHead;
    public long AvailableFromSequence => _events.Count == 0 ? 0 : _events[0].Sequence;
    public int MaxEvents { get; }

    /// <summary>
    /// 检查运行归属、严格递增序号及终态约束后保留事件，超出窗口时丢弃最旧事件。
    /// </summary>
    public void Append(AssistantRunEventEnvelope envelope, AssistantRunStatus persistedStatus)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.RunId, _runId, StringComparison.Ordinal)
            || !string.Equals(envelope.TurnId, _turnId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_event_scope_mismatch");
        }
        if (_terminal)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_terminal_event_already_set");

        var expected = _sequenceHead + 1;
        if (envelope.Sequence != expected)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_event_sequence_gap");
        if (_events.Any(existing => string.Equals(existing.EventId, envelope.EventId, StringComparison.Ordinal)))
            throw new BizResultException(BusinessCodes.Conflict, "assistant_event_duplicate");

        _events.Add(envelope);
        if (_events.Count > MaxEvents)
            _events.RemoveRange(0, _events.Count - MaxEvents);
        _sequenceHead = envelope.Sequence;
        _terminal = IsTerminalEvent(envelope.Type);
        _persistedStatus = persistedStatus;
    }

    /// <summary>
    /// 返回给定序号之后的事件；若请求早于保留窗口，则返回回放缺口信息。
    /// </summary>
    public AssistantRunEventReplayResult ReadAfter(long afterSequence)
    {
        if (afterSequence < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "after_sequence_invalid");

        if (_events.Count > 0 && afterSequence < AvailableFromSequence - 1)
        {
            return new AssistantRunEventReplayResult(
                Array.Empty<AssistantRunEventEnvelope>(),
                new AssistantReplayGap(
                    _runId,
                    afterSequence,
                    AvailableFromSequence,
                    CurrentSequence,
                    _persistedStatus));
        }

        return new AssistantRunEventReplayResult(
            _events.Where(item => item.Sequence > afterSequence).ToArray(),
            null);
    }

    private static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    private static bool IsTerminalEvent(string type)
        => string.Equals(type, AssistantRunEventTypes.RunCompleted, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunFailed, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunCancelled, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunExpired, StringComparison.Ordinal);
}

/// <summary>
/// 事件回放结果，返回可用事件或说明已超出保留窗口的缺口信息。
/// </summary>
public sealed record AssistantRunEventReplayResult(
    IReadOnlyList<AssistantRunEventEnvelope> Events,
    AssistantReplayGap? Gap);
