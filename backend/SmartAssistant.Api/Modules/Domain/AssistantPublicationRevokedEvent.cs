#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 发布撤回后的领域事件，记录助手、版本和操作人等审计信息。
/// </summary>
public sealed class AssistantPublicationRevokedEvent : DomainEvent, IDeterministicOutboxIdEvent
{
    public AssistantPublicationRevokedEvent(
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        long pointerRevision,
        string actorUserId)
    {
        var identity = string.Concat(
            tenantId, "\0", orgId, "\0", assistantId, "\0", publishedVersionId, "\0",
            pointerRevision.ToString(CultureInfo.InvariantCulture));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)).ToLowerInvariant();
        Id = $"assistant-publication-revoked:{fingerprint}";
        AssistantId = assistantId;
        TenantId = tenantId;
        OrgId = orgId;
        PublishedVersionId = publishedVersionId;
        PointerRevision = pointerRevision;
        ActorUserId = actorUserId;
    }

    public string AssistantId { get; }
    public string TenantId { get; }
    public string OrgId { get; }
    public string PublishedVersionId { get; }
    public long PointerRevision { get; }
    public string ActorUserId { get; }
}
