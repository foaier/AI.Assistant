#nullable enable
// Spec: specs/136-public-assistant/spec.md, S2-B event broker and transports.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure.AssistantTransport;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手事件事件代理行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantEventBrokerTests
{
    /// <summary>
    /// 验证事件代理能够回放历史，并在终态事件之后结束订阅。
    /// </summary>
    [Fact]
    public async Task Broker_replays_events_and_completes_after_terminal_event()
    {
        var broker = new InMemoryAssistantRunEventBroker();
        await broker.PublishAsync(Event(1, AssistantRunEventTypes.RunAccepted));
        await broker.PublishAsync(Event(2, AssistantRunEventTypes.RunCompleted));

        var received = new List<AssistantRunEventEnvelope>();
        await foreach (var item in broker.SubscribeAsync(new AssistantRunResumeRequest(
            "run-1", 0, AssistantTransportKind.Sse)))
        {
            received.Add(item);
        }

        Assert.Equal(new[] { 1L, 2L }, received.Select(item => item.Sequence));
        Assert.Equal(AssistantRunEventTypes.RunCompleted, received[^1].Type);
    }

    /// <summary>
    /// 验证游标超出回放窗口时会返回明确的回放缺口。
    /// </summary>
    [Fact]
    public async Task Broker_emits_explicit_replay_gap_when_cursor_falls_outside_window()
    {
        var broker = new InMemoryAssistantRunEventBroker(replayWindowSize: 2);
        await broker.PublishAsync(Event(1, AssistantRunEventTypes.RunAccepted));
        await broker.PublishAsync(Event(2, AssistantRunEventTypes.RunStarted));
        await broker.PublishAsync(Event(3, AssistantRunEventTypes.RunCompleted));

        var received = new List<AssistantRunEventEnvelope>();
        await foreach (var item in broker.SubscribeAsync(new AssistantRunResumeRequest(
            "run-1", 0, AssistantTransportKind.WebSocket)))
        {
            received.Add(item);
        }

        var gap = Assert.Single(received);
        Assert.Equal(AssistantRunEventTypes.ReplayGap, gap.Type);
        Assert.Equal(3L, gap.Payload.GetProperty("CurrentSequence").GetInt64());
        Assert.Equal(2L, gap.Payload.GetProperty("AvailableFromSequence").GetInt64());
    }

    /// <summary>
    /// 验证实时订阅无需重复执行模型供应商调用。
    /// </summary>
    [Fact]
    public async Task Broker_supports_live_subscriber_without_replaying_provider_work()
    {
        var broker = new InMemoryAssistantRunEventBroker();
        var received = new List<AssistantRunEventEnvelope>();
        var consuming = Task.Run(async () =>
        {
            await foreach (var item in broker.SubscribeAsync(new AssistantRunResumeRequest(
                "run-live", 0, AssistantTransportKind.Sse)))
            {
                received.Add(item);
            }
        });

        await Task.Delay(50);
        await broker.PublishAsync(Event(1, AssistantRunEventTypes.RunAccepted, "run-live"));
        await broker.PublishAsync(Event(2, AssistantRunEventTypes.RunCompleted, "run-live"));
        await consuming.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new[] { AssistantRunEventTypes.RunAccepted, AssistantRunEventTypes.RunCompleted },
            received.Select(item => item.Type));
    }

    /// <summary>
    /// 验证事件代理拒绝运行或轮次范围不匹配的事件。
    /// </summary>
    [Fact]
    public async Task Broker_rejects_wrong_run_or_turn_scope()
    {
        var broker = new InMemoryAssistantRunEventBroker();
        await broker.PublishAsync(Event(1, AssistantRunEventTypes.RunAccepted));

        await Assert.ThrowsAsync<BizResultException>(() =>
            broker.PublishAsync(Event(2, AssistantRunEventTypes.MessageDelta, turnId: "turn-other")));
    }

    /// <summary>
    /// 验证回放缺口不会占用正常事件序号。
    /// </summary>
    [Fact]
    public async Task Broker_does_not_emit_replay_gap_as_a_canonical_sequence_event()
    {
        var broker = new InMemoryAssistantRunEventBroker(replayWindowSize: 1);
        await broker.PublishAsync(Event(1, AssistantRunEventTypes.RunAccepted));
        await broker.PublishAsync(Event(2, AssistantRunEventTypes.RunStarted));

        var item = await broker.SubscribeAsync(new AssistantRunResumeRequest(
            "run-1", 0, AssistantTransportKind.Sse)).FirstAsync();

        Assert.Equal(AssistantRunEventTypes.ReplayGap, item.Type);
        Assert.Equal(3, item.Sequence);
        Assert.NotEqual(2, item.Sequence);
    }

    /// <summary>
    /// 验证事件相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantRunEventEnvelope Event(
        long sequence,
        string type,
        string runId = "run-1",
        string turnId = "turn-1")
        => AssistantRunEventEnvelope.Create(
            $"event-{runId}-{sequence}",
            runId,
            turnId,
            sequence,
            type,
            DateTime.UtcNow,
            new { text = "ok" });
}
