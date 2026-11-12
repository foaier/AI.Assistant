#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260927-006-public-assistant-publication-authority.md
using System;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

public sealed class AssistantPublicationTests
{
    [Fact]
    public void Publish_snapshot_is_immutable_and_hash_is_stable()
    {
        var draft = AssistantDraft.Create(
            "assistant-1", "tenant-1", "org-1", "客服", "说明",
            "{ \"enabled\": true, \"n\": 1 }", "actor-1", null, DateTime.UtcNow);

        var version = AssistantPublishedVersion.Create(draft, 1, "actor-1", DateTime.UtcNow, draft.DraftJson);

        Assert.Equal("assistant-1", version.AssistantKey);
        Assert.Equal("{\"enabled\":true,\"n\":1}", version.SnapshotJson);
        Assert.Equal(64, version.SnapshotHash.Length);
        Assert.False(version.IsRevoked);
    }

    [Fact]
    public void Pointer_advance_requires_revision_and_rejects_cross_scope()
    {
        var now = DateTime.UtcNow;
        var firstDraft = AssistantDraft.Create("assistant-1", "tenant-1", "org-1", "客服", null, "{}", "actor-1", null, now);
        var pointer = AssistantPublicationPointer.Create(
            AssistantPublishedVersion.Create(firstDraft, 1, "actor-1", now, firstDraft.DraftJson), "actor-1", now);
        var secondDraft = AssistantDraft.Create("assistant-2", "tenant-1", "org-2", "客服2", null, "{}", "actor-1", null, now);
        var secondVersion = AssistantPublishedVersion.Create(secondDraft, 2, "actor-1", now, secondDraft.DraftJson);

        Assert.Throws<BizResultException>(() => pointer.Advance(secondVersion, 1, "actor-1", now));
        Assert.Throws<BizResultException>(() => pointer.Advance(
            AssistantPublishedVersion.Create(firstDraft, 2, "actor-1", now, firstDraft.DraftJson), 0, "actor-1", now));
    }

    [Fact]
    public void Revoke_clears_pointer_and_marks_version_terminal()
    {
        var now = DateTime.UtcNow;
        var draft = AssistantDraft.Create("assistant-1", "tenant-1", "org-1", "客服", null, "{}", "actor-1", null, now);
        var version = AssistantPublishedVersion.Create(draft, 1, "actor-1", now, draft.DraftJson);
        var pointer = AssistantPublicationPointer.Create(version, "actor-1", now);

        var revokedId = pointer.Revoke(1, "actor-2", now.AddMinutes(1));
        version.Revoke("actor-2", now.AddMinutes(1));

        Assert.Equal(version.Id, revokedId);
        Assert.Null(pointer.CurrentPublishedVersionId);
        Assert.Equal(AssistantPublicationPointerStatus.Revoked, pointer.Status);
        Assert.True(version.IsRevoked);
        Assert.Throws<BizResultException>(() => pointer.Revoke(2, "actor-2", now.AddMinutes(2)));
    }

    [Fact]
    public void Revocation_event_id_fits_outbox_key_and_is_deterministic()
    {
        AssistantPublicationRevokedEvent Create(long revision) => new(
            "assistant-12345678901234567890123456789012",
            "tenant-12345678901234567890123456789012",
            "org-12345678901234567890123456789012",
            "version-12345678901234567890123456789012",
            revision,
            "actor-1");

        var first = Create(7);
        var replay = Create(7);
        var nextRevision = Create(8);

        Assert.InRange(first.Id.Length, 1, 64);
        Assert.Equal(first.Id, replay.Id);
        Assert.NotEqual(first.Id, nextRevision.Id);
    }
}
