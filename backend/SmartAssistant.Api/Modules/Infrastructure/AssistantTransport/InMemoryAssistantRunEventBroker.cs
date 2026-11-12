#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Infrastructure.AssistantTransport;

/// <summary>
/// 在当前进程内发布、保留和订阅运行事件，适用于单实例部署。
/// 事件状态不会跨进程共享，多实例场景需使用共享事件代理。
/// </summary>
public sealed class InMemoryAssistantRunEventBroker : IRunEventBroker
{
    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    private readonly int _replayWindowSize;

    public InMemoryAssistantRunEventBroker(int replayWindowSize = 256)
    {
        if (replayWindowSize < 1)
            throw new ArgumentOutOfRangeException(nameof(replayWindowSize));
        _replayWindowSize = replayWindowSize;
    }

    /// <summary>
    /// 按运行范围发布有序事件，并维护恢复订阅所需的事件进度。
    /// </summary>
    public Task PublishAsync(
        AssistantRunEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        var state = _runs.GetOrAdd(
            envelope.RunId,
            _ => new RunState(envelope.RunId, envelope.TurnId, _replayWindowSize));
        state.Publish(envelope);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 先读取指定序号之后的保留事件，再接收后续事件；窗口缺口按统一契约返回。
    /// </summary>
    public async IAsyncEnumerable<AssistantRunEventEnvelope> SubscribeAsync(
        AssistantRunResumeRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_run_required");
        if (request.AfterSequence < 0)
            throw new BizResultException(BusinessCodes.BusinessException, "after_sequence_invalid");

        var state = _runs.GetOrAdd(
            request.RunId,
            _ => new RunState(request.RunId, string.Empty, _replayWindowSize));
        var subscription = state.Subscribe(request.AfterSequence);

        if (subscription.Gap is not null)
        {
            yield return CreateReplayGapEnvelope(state, request.AfterSequence, subscription.Gap);
            yield break;
        }

        foreach (var item in subscription.Events)
            yield return item;

        if (subscription.Terminal)
            yield break;

        try
        {
            await foreach (var item in subscription.Channel!.Reader.ReadAllAsync(cancellationToken))
            {
                if (item.Sequence <= request.AfterSequence)
                    continue;
                yield return item;
                if (IsTerminal(item.Type))
                    yield break;
            }
        }
        finally
        {
            state.Unsubscribe(subscription.Channel!);
        }
    }

    private static AssistantRunEventEnvelope CreateReplayGapEnvelope(
        RunState state,
        long afterSequence,
        AssistantReplayGap gap)
        => AssistantRunEventEnvelope.Create(
            $"{state.RunId}:replay-gap:{afterSequence}:{gap.CurrentSequence}",
            state.RunId,
            state.TurnId,
            Math.Max(1, gap.CurrentSequence + 1),
            AssistantRunEventTypes.ReplayGap,
            DateTime.UtcNow,
            gap);

    private static bool IsTerminal(string type)
        => type is AssistantRunEventTypes.RunCompleted
            or AssistantRunEventTypes.RunFailed
            or AssistantRunEventTypes.RunCancelled
            or AssistantRunEventTypes.RunExpired;

    private sealed class RunState
    {
        private readonly object _gate = new();
        private readonly int _replayWindowSize;
        private AssistantRunEventReplay? _replay;
        private readonly List<Channel<AssistantRunEventEnvelope>> _subscribers = new();
        private bool _terminal;

        public RunState(string runId, string turnId, int replayWindowSize)
        {
            RunId = runId;
            TurnId = turnId;
            _replayWindowSize = replayWindowSize;
            if (!string.IsNullOrWhiteSpace(turnId))
                _replay = new AssistantRunEventReplay(runId, turnId, maxEvents: replayWindowSize);
        }

        public string RunId { get; }
        public string TurnId { get; private set; }

        public void Publish(AssistantRunEventEnvelope envelope)
        {
            Channel<AssistantRunEventEnvelope>[] subscribers;
            lock (_gate)
            {
                if (string.IsNullOrEmpty(TurnId))
                {
                    TurnId = envelope.TurnId;
                    _replay = new AssistantRunEventReplay(RunId, TurnId, maxEvents: _replayWindowSize);
                }
                else if (!string.Equals(TurnId, envelope.TurnId, StringComparison.Ordinal))
                {
                    throw new BizResultException(BusinessCodes.Forbidden, "assistant_event_scope_mismatch");
                }
                _replay!.Append(envelope, InferStatus(envelope.Type));
                _terminal = IsTerminal(envelope.Type);
                subscribers = _subscribers.ToArray();
            }

            foreach (var subscriber in subscribers)
            {
                if (!subscriber.Writer.TryWrite(envelope) && _terminal)
                    subscriber.Writer.TryComplete();
            }

            if (_terminal)
            {
                foreach (var subscriber in subscribers)
                    subscriber.Writer.TryComplete();
            }
        }

        public Subscription Subscribe(long afterSequence)
        {
            lock (_gate)
            {
                if (_replay is null)
                {
                    var pending = Channel.CreateUnbounded<AssistantRunEventEnvelope>(
                        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                    _subscribers.Add(pending);
                    return new Subscription(Array.Empty<AssistantRunEventEnvelope>(), null, false, pending);
                }

                var replay = _replay.ReadAfter(afterSequence);
                if (replay.Gap is not null)
                    return new Subscription(replay.Events, replay.Gap, _terminal, null);
                if (_terminal)
                    return new Subscription(replay.Events, null, true, null);

                var channel = Channel.CreateUnbounded<AssistantRunEventEnvelope>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                _subscribers.Add(channel);
                return new Subscription(replay.Events, null, false, channel);
            }
        }

        public void Unsubscribe(Channel<AssistantRunEventEnvelope> channel)
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
                channel.Writer.TryComplete();
            }
        }

        private static AssistantRunStatus InferStatus(string type)
            => type switch
            {
                AssistantRunEventTypes.RunCompleted => AssistantRunStatus.Completed,
                AssistantRunEventTypes.RunFailed => AssistantRunStatus.Failed,
                AssistantRunEventTypes.RunCancelled => AssistantRunStatus.Cancelled,
                AssistantRunEventTypes.RunExpired => AssistantRunStatus.Expired,
                AssistantRunEventTypes.RunStarted => AssistantRunStatus.Running,
                _ => AssistantRunStatus.Accepted
            };
    }

    private sealed record Subscription(
        IReadOnlyList<AssistantRunEventEnvelope> Events,
        AssistantReplayGap? Gap,
        bool Terminal,
        Channel<AssistantRunEventEnvelope>? Channel);
}
