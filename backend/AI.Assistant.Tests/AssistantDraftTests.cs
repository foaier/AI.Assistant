// Spec: specs/136-public-assistant/spec.md, S0 assistant draft contract.
#nullable enable
using System;
using AI.Assistant.Services.Domain;
using AI.Assistant.Platform;
using Xunit;

namespace AI.Assistant.Tests;

/// <summary>
/// 验证助手草稿行为和边界条件的自动化测试集合。
/// </summary>
public sealed class AssistantDraftTests
{
    /// <summary>
    /// 验证创建草稿会规范化 JSON，并将初始修订设为一。
    /// </summary>
    [Fact]
    public void Create_normalizes_json_and_starts_revision_at_one()
    {
        var draft = AssistantDraft.Create("a1", "客服", null, "{ \"enabled\": true }", "k1");

        Assert.Equal(1, draft.DraftRevision);
        Assert.Equal("{\"enabled\":true}", draft.DraftJson);
        Assert.Equal("k1", draft.CreationIdempotencyKey);
    }

    /// <summary>
    /// 验证保存要求预期修订匹配，并且只递增一次修订号。
    /// </summary>
    [Fact]
    public void Save_requires_expected_revision_and_increments_once()
    {
        var draft = AssistantDraft.Create("a1", "客服", null, "{}", null);

        draft.Save("新客服", "说明", "{\"web\":true}", 1);

        Assert.Equal(2, draft.DraftRevision);
        Assert.Equal("新客服", draft.Name);
        Assert.Throws<BizResultException>(() => draft.Save("再次保存", null, "{}", 1));
    }

    /// <summary>
    /// 验证无效 JSON 在持久化之前被拒绝。
    /// </summary>
    [Fact]
    public void Invalid_json_is_rejected_before_persistence()
    {
        var exception = Assert.Throws<BizResultException>(() =>
            AssistantDraft.Create("a1", "客服", null, "not-json", null));

        Assert.Contains("valid JSON", exception.BusinessMessage);
    }

    /// <summary>
    /// 验证草稿保存事件具有稳定的发件箱标识。
    /// </summary>
    [Fact]
    public void Saved_event_has_deterministic_outbox_id()
    {
        var @event = new AssistantDraftSavedEvent("a1", 3);

        Assert.Equal("assistant-draft-saved:a1:3", @event.Id);
        Assert.IsAssignableFrom<AI.Assistant.Platform.Events.IDeterministicOutboxIdEvent>(@event);
    }

}
