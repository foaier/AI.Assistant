// Spec: specs/136-public-assistant/requests/REQ-20260925-004-public-assistant-s2-chat-runtime-admission.md,
// S2-B transport envelope, replay/resume and stream-ticket contract.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure.AssistantTransport;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantTransportTests
{
    [Fact]
    public void Sse_and_websocket_serializers_share_the_same_event_json()
    {
        var envelope = AssistantRunEventEnvelope.Create(
            "evt-1",
            "run-1",
            "turn-1",
            7,
            AssistantRunEventTypes.MessageDelta,
            DateTime.UtcNow,
            new { messageId = "msg-1", delta = "hello" });
        var serializer = new AssistantRunEventTransportSerializer();

        var websocketJson = serializer.SerializeWebSocket(envelope);
        var sseFrame = serializer.SerializeSse(envelope);
        var sseData = sseFrame
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];

        using var websocketDocument = JsonDocument.Parse(websocketJson);
        using var sseDocument = JsonDocument.Parse(sseData);
        Assert.True(JsonElement.DeepEquals(
            websocketDocument.RootElement,
            sseDocument.RootElement));
        Assert.Contains("id: 7", sseFrame, StringComparison.Ordinal);
        Assert.Contains("event: message.delta", sseFrame, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_returns_events_after_cursor_and_explicit_gap()
    {
        var replay = new AssistantRunEventReplay(
            "run-1",
            "turn-1",
            initialSequence: 1);
        replay.Append(
            AssistantRunEventEnvelope.Create(
                "evt-2",
                "run-1",
                "turn-1",
                2,
                AssistantRunEventTypes.MessageDelta,
                DateTime.UtcNow,
                new { delta = "two" }),
            AssistantRunStatus.Running);
        replay.Append(
            AssistantRunEventEnvelope.Create(
                "evt-3",
                "run-1",
                "turn-1",
                3,
                AssistantRunEventTypes.RunCompleted,
                DateTime.UtcNow,
                new { }),
            AssistantRunStatus.Completed);

        var resumed = replay.ReadAfter(2);
        Assert.Null(resumed.Gap);
        Assert.Single(resumed.Events);
        Assert.Equal(3, resumed.Events[0].Sequence);

        var gap = replay.ReadAfter(0);
        Assert.NotNull(gap.Gap);
        Assert.Equal(2, gap.Gap!.AvailableFromSequence);
        Assert.Equal(AssistantRunStatus.Completed, gap.Gap.PersistedStatus);
    }

    [Fact]
    public void Replay_rejects_scope_and_sequence_gaps()
    {
        var replay = new AssistantRunEventReplay("run-1", "turn-1");
        var scopeError = Assert.Throws<BizResultException>(() =>
            replay.Append(
                AssistantRunEventEnvelope.Create(
                    "evt-1",
                    "run-2",
                    "turn-1",
                    1,
                    AssistantRunEventTypes.RunAccepted,
                    DateTime.UtcNow,
                    new { }),
                AssistantRunStatus.Accepted));
        Assert.Equal("assistant_event_scope_mismatch", scopeError.BusinessMessage);

        var sequenceError = Assert.Throws<BizResultException>(() =>
            replay.Append(
                AssistantRunEventEnvelope.Create(
                    "evt-2",
                    "run-1",
                    "turn-1",
                    2,
                    AssistantRunEventTypes.RunAccepted,
                    DateTime.UtcNow,
                    new { }),
                AssistantRunStatus.Accepted));
        Assert.Equal("assistant_event_sequence_gap", sequenceError.BusinessMessage);
    }

    [Fact]
    public async Task Signed_stream_ticket_validates_scope_and_transport()
    {
        var service = new SignedAssistantStreamTicketService(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var scope = new AssistantScopeSnapshot(
            "assistant-1",
            "tenant-1",
            "org-1",
            "published-1",
            AssistantExecutionMode.Public);
        var ticket = await service.IssueAsync(
            scope,
            "session-1",
            "conversation-1",
            "turn-1",
            "run-1",
            new HashSet<AssistantTransportKind>
            {
                AssistantTransportKind.Sse,
                AssistantTransportKind.WebSocket
            },
            DateTime.UtcNow.AddMinutes(5));

        var validated = await service.ValidateAsync(
            ticket.Value,
            AssistantTransportKind.WebSocket,
            scope,
            "session-1",
            "conversation-1",
            "turn-1",
            "run-1",
            DateTime.UtcNow);

        Assert.Equal(ticket.RunId, validated.RunId);
        Assert.Equal(ticket.ExpiresAt, validated.ExpiresAt);
        Assert.Contains(AssistantTransportKind.Sse, validated.AllowedTransports);
    }

    [Fact]
    public async Task Signed_stream_ticket_rejects_tampering_scope_transport_and_expiry()
    {
        var service = new SignedAssistantStreamTicketService(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var scope = new AssistantScopeSnapshot(
            "assistant-1",
            "tenant-1",
            "org-1",
            "published-1",
            AssistantExecutionMode.Public);
        var ticket = await service.IssueAsync(
            scope,
            "session-1",
            "conversation-1",
            "turn-1",
            "run-1",
            new HashSet<AssistantTransportKind> { AssistantTransportKind.Sse },
            DateTime.UtcNow.AddMinutes(5));

        var tampered = ticket.Value[..^1] + (ticket.Value[^1] == 'a' ? "b" : "a");
        var tamperError = await Assert.ThrowsAsync<BizResultException>(() =>
            service.ValidateAsync(
                tampered,
                AssistantTransportKind.Sse,
                scope,
                "session-1",
                "conversation-1",
                "turn-1",
                "run-1",
                DateTime.UtcNow));
        Assert.Equal("assistant_stream_ticket_invalid", tamperError.BusinessMessage);

        var transportError = await Assert.ThrowsAsync<BizResultException>(() =>
            service.ValidateAsync(
                ticket.Value,
                AssistantTransportKind.WebSocket,
                scope,
                "session-1",
                "conversation-1",
                "turn-1",
                "run-1",
                DateTime.UtcNow));
        Assert.Equal("assistant_stream_transport_not_allowed", transportError.BusinessMessage);

        var scopeError = await Assert.ThrowsAsync<BizResultException>(() =>
            service.ValidateAsync(
                ticket.Value,
                AssistantTransportKind.Sse,
                scope,
                "session-2",
                "conversation-1",
                "turn-1",
                "run-1",
                DateTime.UtcNow));
        Assert.Equal("assistant_stream_ticket_scope_mismatch", scopeError.BusinessMessage);

        var expiryError = await Assert.ThrowsAsync<BizResultException>(() =>
            service.ValidateAsync(
                ticket.Value,
                AssistantTransportKind.Sse,
                scope,
                "session-1",
                "conversation-1",
                "turn-1",
                "run-1",
                DateTime.UtcNow.AddMinutes(6)));
        Assert.Equal("assistant_stream_ticket_expired", expiryError.BusinessMessage);
    }
}
