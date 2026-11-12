#nullable enable
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 发布成功后的领域事件，记录新发布版本及操作信息。
/// </summary>
public sealed class AssistantPublishedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    public AssistantPublishedEvent(
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        long versionNo,
        string actorUserId)
    {
        Id = $"assistant-published:{assistantId}:{versionNo}";
        AssistantId = assistantId;
        TenantId = tenantId;
        OrgId = orgId;
        PublishedVersionId = publishedVersionId;
        VersionNo = versionNo;
        ActorUserId = actorUserId;
    }

    public string AssistantId { get; }
    public string TenantId { get; }
    public string OrgId { get; }
    public string PublishedVersionId { get; }
    public long VersionNo { get; }
    public string ActorUserId { get; }
}
