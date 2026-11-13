#nullable enable
using AI.Assistant.Models.Runtime;
using AI.Assistant.Models.Transport;
using AI.Assistant.Infrastructure;

namespace AI.Assistant.Services.Repositorys.AssistantTransport;

/// <summary>
/// 维护有限长度的运行事件回放窗口，并检测顺序、重复和终态约束。
/// 仅负责回放语义，不直接访问网络或缓存服务。
/// </summary>
public sealed class AssistantRunEventReplay
{
    /// <summary>
    /// 保存助手执行事件回放在处理事件时所需的数据或状态。
    /// </summary>
    private readonly List<AssistantRunEventEnvelope> _events = new();
    /// <summary>
    /// 保存助手执行事件回放在处理执行时所需的数据或状态。
    /// </summary>
    private readonly string _runId;
    /// <summary>
    /// 保存助手执行事件回放在处理轮次时所需的数据或状态。
    /// </summary>
    private readonly string _turnId;
    /// <summary>
    /// 保存助手执行事件回放在处理序号时所需的数据或状态。
    /// </summary>
    private long _sequenceHead;
    /// <summary>
    /// 保存助手执行事件回放在处理终态时所需的数据或状态。
    /// </summary>
    private bool _terminal;
    /// <summary>
    /// 保存助手执行状态依赖，供助手执行事件回放执行相关操作。
    /// </summary>
    private AssistantRunStatus _persistedStatus;

    /// <summary>
    /// 接收并保存助手执行事件回放执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
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

    /// <summary>
    /// 记录当前已分配或处理的事件序号。
    /// </summary>
    public long CurrentSequence => _sequenceHead;
    /// <summary>
    /// 记录事件回放窗口中最早可读取的序号。
    /// </summary>
    public long AvailableFromSequence => _events.Count == 0 ? 0 : _events[0].Sequence;
    /// <summary>
    /// 限制单次回放或事件窗口保留的事件数量。
    /// </summary>
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

    /// <summary>
    /// 校验字符串必填，并在返回前去除首尾空白。
    /// </summary>
    private static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    /// <summary>
    /// 判断是否满足终态事件。
    /// </summary>
    private static bool IsTerminalEvent(string type)
        => string.Equals(type, AssistantRunEventTypes.RunCompleted, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunFailed, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunCancelled, StringComparison.Ordinal)
            || string.Equals(type, AssistantRunEventTypes.RunExpired, StringComparison.Ordinal);
}
