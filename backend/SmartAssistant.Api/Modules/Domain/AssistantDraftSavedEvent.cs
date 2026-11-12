#nullable enable
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 草稿保存后产生的领域事件，以确定的草稿修订标识关联 Outbox 记录。
/// </summary>
public sealed class AssistantDraftSavedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    public AssistantDraftSavedEvent(string assistantId, string tenantId, string orgId, long revision, string actorUserId)
    {
        Id = $"assistant-draft-saved:{assistantId}:{revision}";
        AssistantId = assistantId;
        TenantId = tenantId;
        OrgId = orgId;
        Revision = revision;
        ActorUserId = actorUserId;
    }

    public string AssistantId { get; }
    public string TenantId { get; }
    public string OrgId { get; }
    public long Revision { get; }
    public string ActorUserId { get; }
}
