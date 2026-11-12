#nullable enable
using System.Collections.Concurrent;
using System.Threading.Channels;
using SmartAssistant.Models.Runtime;
using SmartAssistant.Models.Transport;
using SmartAssistant.Platform;
using SmartAssistant.Services.Contracts;

namespace SmartAssistant.Services.Infrastructure.AssistantTransport;

/// <summary>
/// 在当前进程内发布、保留和订阅运行事件，适用于单实例部署。
/// 事件状态不会跨进程共享，多实例场景需使用共享事件代理。
/// </summary>
public sealed class InMemoryAssistantRunEventBroker : IRunEventBroker
{
    /// <summary>
    /// 保存助手执行事件事件代理在处理执行时所需的数据或状态。
    /// </summary>
    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    /// <summary>
    /// 保存助手执行事件事件代理在处理回放窗口大小时所需的数据或状态。
    /// </summary>
    private readonly int _replayWindowSize;

    /// <summary>
    /// 接收并保存助手执行事件事件代理执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
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

    /// <summary>
    /// 创建包含当前回放边界和运行状态的序号缺口事件。
    /// </summary>
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
            DateTime.Now,
            gap);

    /// <summary>
    /// 判断事件类型或运行状态是否已经进入不可逆终态。
    /// </summary>
    private static bool IsTerminal(string type)
        => type is AssistantRunEventTypes.RunCompleted
            or AssistantRunEventTypes.RunFailed
            or AssistantRunEventTypes.RunCancelled
            or AssistantRunEventTypes.RunExpired;

    /// <summary>
    /// 封装执行状态相关的内部状态或数据，供所属组件完成相应处理。
    /// </summary>
    private sealed class RunState
    {
        /// <summary>
        /// 提供执行状态访问共享状态时使用的同步锁。
        /// </summary>
        private readonly object _gate = new();
        /// <summary>
        /// 保存执行状态在处理回放窗口大小时所需的数据或状态。
        /// </summary>
        private readonly int _replayWindowSize;
        /// <summary>
        /// 保存助手执行事件回放依赖，供执行状态执行相关操作。
        /// </summary>
        private AssistantRunEventReplay? _replay;
        /// <summary>
        /// 保存执行状态当前等待事件的订阅者，供事件发布时唤醒。
        /// </summary>
        private readonly List<Channel<AssistantRunEventEnvelope>> _subscribers = new();
        /// <summary>
        /// 保存执行状态在处理终态时所需的数据或状态。
        /// </summary>
        private bool _terminal;

        /// <summary>
        /// 接收并保存执行状态执行所需的依赖，建立后续操作使用的运行状态。
        /// </summary>
        public RunState(string runId, string turnId, int replayWindowSize)
        {
            RunId = runId;
            TurnId = turnId;
            _replayWindowSize = replayWindowSize;
            if (!string.IsNullOrWhiteSpace(turnId))
                _replay = new AssistantRunEventReplay(runId, turnId, maxEvents: replayWindowSize);
        }

        /// <summary>
        /// 关联产生此事件、消息或工具调用的执行运行。
        /// </summary>
        public string RunId { get; }
        /// <summary>
        /// 关联此记录所属的用户请求轮次。
        /// </summary>
        public string TurnId { get; private set; }

        /// <summary>
        /// 校验事件顺序后写入有界回放窗口，并唤醒等待中的订阅者。
        /// </summary>
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

        /// <summary>
        /// 先发送保留窗口中可恢复的事件，再持续等待新事件或终态。
        /// </summary>
        public AssistantRunEventSubscription Subscribe(long afterSequence)
        {
            lock (_gate)
            {
                if (_replay is null)
                {
                    var pending = Channel.CreateUnbounded<AssistantRunEventEnvelope>(
                        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                    _subscribers.Add(pending);
                    return new AssistantRunEventSubscription(Array.Empty<AssistantRunEventEnvelope>(), null, false, pending);
                }

                var replay = _replay.ReadAfter(afterSequence);
                if (replay.Gap is not null)
                    return new AssistantRunEventSubscription(replay.Events, replay.Gap, _terminal, null);
                if (_terminal)
                    return new AssistantRunEventSubscription(replay.Events, null, true, null);

                var channel = Channel.CreateUnbounded<AssistantRunEventEnvelope>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                _subscribers.Add(channel);
                return new AssistantRunEventSubscription(replay.Events, null, false, channel);
            }
        }

        /// <summary>
        /// 移除订阅并释放等待资源，避免断开的连接继续被通知。
        /// </summary>
        public void Unsubscribe(Channel<AssistantRunEventEnvelope> channel)
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
                channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// 从已记录的终态事件推断运行的当前持久状态。
        /// </summary>
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
}
