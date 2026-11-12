#nullable enable
using System;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 发布指针的有效或撤回状态。
/// </summary>
public enum AssistantPublicationPointerStatus
{
    Revoked = 0,
    Active = 1
}

/// <summary>
/// 记录公开助手当前指向的发布版本，并用修订号保护并发切换。
/// </summary>
public sealed class AssistantPublicationPointer
{
    private AssistantPublicationPointer() { }

    public string Id { get; private set; } = string.Empty;
    public string AssistantKey { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    /// <summary>
    /// 当前公开可用的发布版本标识；未发布或撤回后可为空。
    /// </summary>
    public string? CurrentPublishedVersionId { get; private set; }
    public long CurrentVersionNo { get; private set; }
    /// <summary>
    /// 发布指针的乐观并发修订号，发布与撤回都需校验该值。
    /// </summary>
    public long PointerRevision { get; private set; }
    public AssistantPublicationPointerStatus Status { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public static AssistantPublicationPointer Create(
        AssistantPublishedVersion version,
        string actorUserId,
        DateTime now)
    {
        if (version is null) throw new ArgumentNullException(nameof(version));
        Require(actorUserId, nameof(actorUserId));
        return new AssistantPublicationPointer
        {
            Id = version.AssistantId,
            AssistantKey = version.AssistantKey,
            AssistantId = version.AssistantId,
            TenantId = version.TenantId,
            OrgId = version.OrgId,
            CurrentPublishedVersionId = version.Id,
            CurrentVersionNo = version.VersionNo,
            PointerRevision = 1,
            Status = AssistantPublicationPointerStatus.Active,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId
        };
    }

    public static AssistantPublicationPointer Rehydrate(
        string id,
        string assistantKey,
        string assistantId,
        string tenantId,
        string orgId,
        string? currentPublishedVersionId,
        long currentVersionNo,
        long pointerRevision,
        AssistantPublicationPointerStatus status,
        bool isDeleted,
        DateTime? deletedAt,
        string? deletedBy,
        DateTime createdAt,
        string createdBy,
        DateTime updatedAt,
        string updatedBy)
        => new()
        {
            Id = id,
            AssistantKey = assistantKey,
            AssistantId = assistantId,
            TenantId = tenantId,
            OrgId = orgId,
            CurrentPublishedVersionId = currentPublishedVersionId,
            CurrentVersionNo = currentVersionNo,
            PointerRevision = pointerRevision,
            Status = status,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy
        };

    /// <summary>
    /// 校验预期指针修订后切换当前发布版本，并递增指针修订号。
    /// </summary>
    public void Advance(AssistantPublishedVersion version, long expectedRevision, string actorUserId, DateTime now)
    {
        if (version is null) throw new ArgumentNullException(nameof(version));
        Require(actorUserId, nameof(actorUserId));
        if (expectedRevision != PointerRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        if (!string.Equals(version.AssistantId, AssistantId, StringComparison.Ordinal)
            || !string.Equals(version.TenantId, TenantId, StringComparison.Ordinal)
            || !string.Equals(version.OrgId, OrgId, StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_publication_scope_mismatch");

        CurrentPublishedVersionId = version.Id;
        CurrentVersionNo = version.VersionNo;
        PointerRevision++;
        Status = AssistantPublicationPointerStatus.Active;
        UpdatedAt = now;
        UpdatedBy = actorUserId;
    }

    /// <summary>
    /// 在预期修订匹配时撤回当前发布指针。
    /// </summary>
    public string Revoke(long expectedRevision, string actorUserId, DateTime now)
    {
        Require(actorUserId, nameof(actorUserId));
        if (expectedRevision != PointerRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        if (CurrentPublishedVersionId is null || Status != AssistantPublicationPointerStatus.Active)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");

        var revokedVersionId = CurrentPublishedVersionId;
        CurrentPublishedVersionId = null;
        PointerRevision++;
        Status = AssistantPublicationPointerStatus.Revoked;
        UpdatedAt = now;
        UpdatedBy = actorUserId;
        return revokedVersionId;
    }

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();
}
