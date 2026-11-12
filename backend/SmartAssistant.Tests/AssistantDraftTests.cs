// Spec: specs/136-public-assistant/spec.md, S0 assistant draft contract.
#nullable enable
using System;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantDraftTests
{
    [Fact]
    public void Create_normalizes_json_and_starts_revision_at_one()
    {
        var draft = AssistantDraft.Create("a1", "t1", "o1", "客服", null, "{ \"enabled\": true }", "u1", "k1", DateTime.UtcNow);

        Assert.Equal(1, draft.DraftRevision);
        Assert.Equal("{\"enabled\":true}", draft.DraftJson);
        Assert.Equal("k1", draft.CreationIdempotencyKey);
    }

    [Fact]
    public void Save_requires_expected_revision_and_increments_once()
    {
        var draft = AssistantDraft.Create("a1", "t1", "o1", "客服", null, "{}", "u1", null, DateTime.UtcNow);

        draft.Save("新客服", "说明", "{\"web\":true}", 1, "u2", DateTime.UtcNow);

        Assert.Equal(2, draft.DraftRevision);
        Assert.Equal("新客服", draft.Name);
        Assert.Throws<BizResultException>(() => draft.Save("再次保存", null, "{}", 1, "u3", DateTime.UtcNow));
    }

    [Fact]
    public void Invalid_json_is_rejected_before_persistence()
    {
        var exception = Assert.Throws<BizResultException>(() =>
            AssistantDraft.Create("a1", "t1", "o1", "客服", null, "not-json", "u1", null, DateTime.UtcNow));

        Assert.Contains("valid JSON", exception.BusinessMessage);
    }

    [Fact]
    public void Saved_event_has_deterministic_outbox_id()
    {
        var @event = new AssistantDraftSavedEvent("a1", "t1", "o1", 3, "u1");

        Assert.Equal("assistant-draft-saved:a1:3", @event.Id);
        Assert.IsAssignableFrom<SmartAssistant.Platform.Events.IDeterministicOutboxIdEvent>(@event);
    }

    [Fact]
    public void Delete_marks_the_draft_as_a_storage_tombstone()
    {
        var now = DateTime.UtcNow;
        var draft = AssistantDraft.Create("a1", "t1", "o1", "客服", null, "{}", "u1", null, now);

        draft.Delete("u2", now.AddMinutes(1));

        Assert.True(draft.IsDeleted);
        Assert.Equal("u2", draft.DeletedBy);
        Assert.Equal(now.AddMinutes(1), draft.DeletedAt);
        Assert.Equal("u2", draft.UpdatedBy);
    }
}
