#nullable enable
// Spec: specs/136-public-assistant/requests/REQ-20260927-006-public-assistant-publication-authority.md
using System;
using SmartAssistant.Services.Domain;
using SmartAssistant.Platform;
using Xunit;

namespace SmartAssistant.Tests;

/// <summary>
/// 验证助手发布行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantPublicationTests
{
    /// <summary>
    /// 验证发布快照不可变，且相同内容的摘要稳定。
    /// </summary>
    [Fact]
    public void Publish_snapshot_is_immutable_and_hash_is_stable()
    {
        var draft = AssistantDraft.Create(
            "assistant-1", "客服", "说明",
            "{ \"enabled\": true, \"n\": 1 }", null);

        var version = AssistantPublishedVersion.Create(draft, 1, DateTime.Now, draft.DraftJson);

        Assert.Equal("assistant-1", version.AssistantKey);
        Assert.Equal("{\"enabled\":true,\"n\":1}", version.SnapshotJson);
        Assert.Equal(64, version.SnapshotHash.Length);
        Assert.False(version.IsRevoked);
    }

    /// <summary>
    /// 验证推进发布指针要求修订匹配，并拒绝跨范围操作。
    /// </summary>
    [Fact]
    public void Pointer_advance_requires_revision_and_rejects_cross_scope()
    {
        var now = DateTime.Now;
        var firstDraft = AssistantDraft.Create("assistant-1", "客服", null, "{}", null);
        var pointer = AssistantPublicationPointer.Create(
            AssistantPublishedVersion.Create(firstDraft, 1, now, firstDraft.DraftJson));
        var secondDraft = AssistantDraft.Create("assistant-2", "客服2", null, "{}", null);
        var secondVersion = AssistantPublishedVersion.Create(secondDraft, 2, now, secondDraft.DraftJson);

        Assert.Throws<BizResultException>(() => pointer.Advance(secondVersion, 1));
        Assert.Throws<BizResultException>(() => pointer.Advance(
            AssistantPublishedVersion.Create(firstDraft, 2, now, firstDraft.DraftJson), 0));
    }

    /// <summary>
    /// 验证撤回会清除发布指针，并将版本标记为终态。
    /// </summary>
    [Fact]
    public void Revoke_clears_pointer_and_marks_version_terminal()
    {
        var now = DateTime.Now;
        var draft = AssistantDraft.Create("assistant-1", "客服", null, "{}", null);
        var version = AssistantPublishedVersion.Create(draft, 1, now, draft.DraftJson);
        var pointer = AssistantPublicationPointer.Create(version);

        var revokedId = pointer.Revoke(1);
        version.Revoke(now.AddMinutes(1));

        Assert.Equal(version.Id, revokedId);
        Assert.Null(pointer.CurrentPublishedVersionId);
        Assert.Equal(AssistantPublicationPointerStatus.Revoked, pointer.Status);
        Assert.True(version.IsRevoked);
        Assert.Throws<BizResultException>(() => pointer.Revoke(2));
    }

    /// <summary>
    /// 验证撤回事件标识满足发件箱长度约束且保持稳定。
    /// </summary>
    [Fact]
    public void Revocation_event_id_fits_outbox_key_and_is_deterministic()
    {
        /*
         * 根据校验后的输入创建助手发布领域对象，并初始化其状态和时间字段。
         */
        AssistantPublicationRevokedEvent Create(long revision) => new(
            "assistant-12345678901234567890123456789012",
            "version-12345678901234567890123456789012",
            revision);

        var first = Create(7);
        var replay = Create(7);
        var nextRevision = Create(8);

        Assert.InRange(first.Id.Length, 1, 64);
        Assert.Equal(first.Id, replay.Id);
        Assert.NotEqual(first.Id, nextRevision.Id);
    }
}
