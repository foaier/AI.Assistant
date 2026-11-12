// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-A/S2-B/S2-C contract and domain invariants.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Platform;
using Xunit;
using SmartAssistant.Models.AI;
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手运行时行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantRuntimeDomainTests
{
    /// <summary>
    /// 验证匿名会话拒绝跨范围访问及过期访问。
    /// </summary>
    [Fact]
    public void Anonymous_session_rejects_cross_scope_access_and_expiry()
    {
        var now = DateTime.UtcNow;
        var session = AnonymousAssistantSession.Create(
            "s1", "a1", "pub1", "subject-hash", "zh-CN",
            AssistantExecutionMode.Public, now, now.AddHours(1));

        session.EnsureAccessible("a1", "pub1", AssistantExecutionMode.Public, now);

        var scopeError = Assert.Throws<BizResultException>(() =>
            session.EnsureAccessible("a2", "pub1", AssistantExecutionMode.Public, now));
        Assert.Equal("assistant_session_scope_mismatch", scopeError.BusinessMessage);

        session.Expire(now.AddMinutes(1));
        var expiredError = Assert.Throws<BizResultException>(() =>
            session.EnsureAccessible("a1", "pub1", AssistantExecutionMode.Public, now.AddMinutes(1)));
        Assert.Equal("assistant_session_not_active", expiredError.BusinessMessage);
    }

    /// <summary>
    /// 验证相同幂等请求复用轮次，不同请求内容则报告冲突。
    /// </summary>
    [Fact]
    public void Turn_idempotency_replays_same_payload_and_conflicts_on_different_payload()
    {
        var now = DateTime.UtcNow;
        var turn = CreateTurn(now, "client-1", "same question");

        var replay = AssistantIdempotency.Evaluate(turn, "text", "same question", "zh-CN");

        Assert.Equal(AssistantIdempotencyOutcome.Replayed, replay.Outcome);
        Assert.Equal(turn.Id, replay.TurnId);

        var conflict = Assert.Throws<BizResultException>(() =>
            AssistantIdempotency.Evaluate(turn, "text", "different question", "zh-CN"));
        Assert.Equal(BusinessCodes.Conflict, conflict.BusinessCode);
        Assert.Equal("assistant_turn_idempotency_payload_conflict", conflict.BusinessMessage);
    }

    /// <summary>
    /// 验证运行终态和最终消息只能提交一次。
    /// </summary>
    [Fact]
    public void Run_terminal_state_and_final_message_can_only_be_written_once()
    {
        var now = DateTime.UtcNow;
        var turn = CreateTurn(now, "client-2", "hello");
        var run = AssistantRun.Create("run-1", turn, "provider-profile", "model-1");
        var message = AssistantMessage.CreateAssistant("msg-1", "conv-1", "turn-1", "run-1", now);

        run.Start(now.AddMilliseconds(1));
        message.AppendAssistantText("hello");
        message.Complete(null, now.AddMilliseconds(2));
        run.Complete(now.AddMilliseconds(3));

        Assert.Equal(AssistantRunStatus.Completed, run.Status);
        Assert.Equal(AssistantMessageStatus.Completed, message.Status);
        Assert.Throws<BizResultException>(() => run.Fail("late_failure", now.AddMilliseconds(4)));
        Assert.Throws<BizResultException>(() => message.Complete(null, now.AddMilliseconds(4)));
    }

    /// <summary>
    /// 验证运行事件序号单调递增，且只允许一个终态。
    /// </summary>
    [Fact]
    public void Run_event_sequence_is_monotonic_and_terminal_is_unique()
    {
        var stream = AssistantRunEventSequence.Create("run-1", "turn-1");
        var first = stream.Append("evt-1", AssistantRunEventTypes.RunAccepted, DateTime.UtcNow, new { });
        var second = stream.Append(
            "evt-2",
            AssistantRunEventTypes.MessageDelta,
            DateTime.UtcNow,
            new { messageId = "msg-1", delta = "hi" });
        var terminal = stream.Append(
            "evt-3",
            AssistantRunEventTypes.RunCompleted,
            DateTime.UtcNow,
            new { });

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(3, terminal.Sequence);
        Assert.True(stream.HasTerminalEvent);

        var error = Assert.Throws<BizResultException>(() =>
            stream.Append("evt-4", AssistantRunEventTypes.Heartbeat, DateTime.UtcNow, new { }));
        Assert.Equal("assistant_run_terminal_event_already_set", error.BusinessMessage);
    }

    /// <summary>
    /// 验证流票据绑定访问范围和传输方式，同时不暴露供应商细节。
    /// </summary>
    [Fact]
    public void Stream_ticket_contract_binds_scope_and_transport_without_provider_details()
    {
        var ticket = new AssistantStreamTicket(
            "opaque-ticket",
            "assistant-1",
            "published-1",
            "session-1",
            "conversation-1",
            "turn-1",
            "run-1",
            AssistantExecutionMode.Public,
            new HashSet<AssistantTransportKind>
            {
                AssistantTransportKind.Sse,
                AssistantTransportKind.WebSocket
            },
            DateTime.UtcNow.AddMinutes(5));

        Assert.Contains(AssistantTransportKind.Sse, ticket.AllowedTransports);
        Assert.Contains(AssistantTransportKind.WebSocket, ticket.AllowedTransports);
        Assert.DoesNotContain("secret", ticket.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证供应商片段不依赖领域状态，且工具参数允许分段输出。
    /// </summary>
    [Fact]
    public void Provider_chunks_are_domain_neutral_and_tool_arguments_can_be_split()
    {
        var first = new AssistantProviderChunk(
            AssistantProviderChunkKind.ToolCallDelta,
            ToolCall: new AssistantProviderToolCallDelta(
                0, "call-1", "public.event.overview.read", "{\"event"));
        var second = new AssistantProviderChunk(
            AssistantProviderChunkKind.ToolCallDelta,
            ToolCall: new AssistantProviderToolCallDelta(
                0, null, null, "Id\":\"event-1\"}"));

        Assert.Equal(AssistantProviderChunkKind.ToolCallDelta, first.Kind);
        Assert.Equal(0, second.ToolCall!.Index);
        Assert.Null(second.ToolCall.ToolCallId);
        Assert.Equal("Id\":\"event-1\"}", second.ToolCall.ArgumentsDelta);
    }

    /// <summary>
    /// 验证事件信封序列化仅包含通用负载，不混入传输层字段。
    /// </summary>
    [Fact]
    public void Event_envelope_serializes_payload_without_transport_specific_fields()
    {
        var envelope = AssistantRunEventEnvelope.Create(
            "evt-1",
            "run-1",
            "turn-1",
            1,
            AssistantRunEventTypes.MessageDelta,
            DateTime.UtcNow,
            new { messageId = "msg-1", delta = "hello" });

        Assert.Equal("msg-1", envelope.Payload.GetProperty("messageId").GetString());
        Assert.Equal("hello", envelope.Payload.GetProperty("delta").GetString());
        Assert.False(envelope.Payload.TryGetProperty("sse", out _));
        Assert.False(envelope.Payload.TryGetProperty("webSocket", out _));
    }

    /// <summary>
    /// 验证创建轮次相关行为、预期结果及错误边界。
    /// </summary>
    private static AssistantTurn CreateTurn(DateTime now, string clientMessageId, string text)
    {
        return AssistantTurn.Create(
            "turn-1",
            "conv-1",
            "session-1",
            "assistant-1",
            "published-1",
            clientMessageId,
            "text",
            text,
            "zh-CN",
            1,
            AssistantExecutionMode.Public);
    }
}
