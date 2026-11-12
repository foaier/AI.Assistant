#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.Common;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Domain;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Events;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 将草稿冻结为发布版本，并通过发布指针切换对外可用版本。
/// 发布与撤回均校验预期修订号，相关写入在同一事务内提交。
/// </summary>
public sealed class AssistantPublicationService : IAssistantPublicationService
{
    private readonly IAssistantDraftRepository _drafts;
    private readonly IAssistantPublicationRepository _publications;
    private readonly IDbContext _dbContext;
    private readonly IDomainEventPublisher _events;
    private readonly AssistantExecutionSnapshotService _snapshots;

    public AssistantPublicationService(
        IAssistantDraftRepository drafts,
        IAssistantPublicationRepository publications,
        IDbContext dbContext,
        IDomainEventPublisher events,
        AssistantExecutionSnapshotService snapshots)
    {
        _drafts = drafts;
        _publications = publications;
        _dbContext = dbContext;
        _events = events;
        _snapshots = snapshots;
    }

    /// <summary>
    /// 校验草稿与指针修订，冻结执行配置并在同一事务中保存发布版本、指针和领域事件。
    /// </summary>
    public async Task<AssistantPublicationResult> PublishAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        PublishAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId, assistantId);
        if (command is null)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_command_required");

        var draft = await _drafts.GetAsync(tenantId, orgId, assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        if (command.ExpectedDraftRevision != draft.DraftRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_draft_revision_conflict");

        var pointer = await _publications.GetPointerAsync(tenantId, orgId, assistantId, cancellationToken);
        var expectedPointerRevision = command.ExpectedPointerRevision;
        if (pointer is null)
        {
            if (expectedPointerRevision is not null && expectedPointerRevision.Value != 0)
                throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        }
        else
        {
            if (expectedPointerRevision is null)
                throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_required");
            if (expectedPointerRevision.Value != pointer.PointerRevision)
                throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_revision_conflict");
        }

        var now = DateTime.UtcNow;
        var versionNo = (pointer?.CurrentVersionNo ?? 0) + 1;
        var snapshotJson = await _snapshots.FreezeAsync(draft, cancellationToken);
        var version = AssistantPublishedVersion.Create(draft, versionNo, actorUserId, now, snapshotJson);

        if (pointer?.CurrentPublishedVersionId is { } currentVersionId
            && pointer.Status == AssistantPublicationPointerStatus.Active)
        {
            var current = await _publications.GetVersionAsync(tenantId, orgId, currentVersionId, cancellationToken);
            if (current is not null && !current.IsRevoked
                && string.Equals(current.SnapshotHash, version.SnapshotHash, StringComparison.Ordinal))
            {
                return new AssistantPublicationResult(Map(current), pointer.PointerRevision, false);
            }
        }

        await _publications.AddVersionAsync(version, cancellationToken);
        if (pointer is null)
        {
            pointer = AssistantPublicationPointer.Create(version, actorUserId, now);
            await _publications.AddPointerAsync(pointer, cancellationToken);
        }
        else
        {
            pointer.Advance(version, expectedPointerRevision!.Value, actorUserId, now);
            await _publications.SavePointerAsync(pointer, expectedPointerRevision.Value, cancellationToken);
        }

        await _events.PublishAsync(new AssistantPublishedEvent(
            version.AssistantId,
            version.TenantId,
            version.OrgId,
            version.Id,
            version.VersionNo,
            actorUserId));
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantPublicationResult(Map(version), pointer.PointerRevision, true);
    }

    /// <summary>
    /// 校验预期发布指针修订，撤回当前版本并记录撤回事件。
    /// </summary>
    public async Task<AssistantPublicationResult> RevokeAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        RevokeAssistantPublicationCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId, assistantId);
        if (command is null)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_command_required");

        var pointer = await _publications.GetPointerAsync(tenantId, orgId, assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");
        var currentVersionId = pointer.CurrentPublishedVersionId
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");
        var version = await _publications.GetVersionAsync(tenantId, orgId, currentVersionId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");

        var now = DateTime.UtcNow;
        var revokedVersionId = pointer.Revoke(command.ExpectedPointerRevision, actorUserId, now);
        if (!string.Equals(revokedVersionId, version.Id, StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_version_conflict");

        version.Revoke(actorUserId, now);
        await _publications.SaveVersionAsync(version, cancellationToken);
        await _publications.SavePointerAsync(pointer, command.ExpectedPointerRevision, cancellationToken);
        await _events.PublishAsync(new AssistantPublicationRevokedEvent(
            pointer.AssistantId,
            pointer.TenantId,
            pointer.OrgId,
            version.Id,
            pointer.PointerRevision,
            actorUserId));
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantPublicationResult(Map(version), pointer.PointerRevision, true);
    }

    /// <summary>
    /// 读取当前指针指向的有效发布版本；未发布或已撤回时返回空值。
    /// </summary>
    public async Task<AssistantPublishedVersionDto?> GetCurrentAsync(
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(orgId)
            || string.IsNullOrWhiteSpace(assistantId))
            return null;
        var pointer = await _publications.GetPointerAsync(tenantId, orgId, assistantId, cancellationToken);
        if (pointer is null || pointer.Status != AssistantPublicationPointerStatus.Active
            || pointer.CurrentPublishedVersionId is null)
            return null;
        var version = await _publications.GetVersionAsync(tenantId, orgId, pointer.CurrentPublishedVersionId, cancellationToken);
        return version is null || version.IsRevoked ? null : Map(version);
    }

    /// <summary>
    /// 读取助手当前的发布状态、指针修订和公开访问标识。
    /// </summary>
    public async Task<AssistantPublicationState> GetStateAsync(string tenantId, string orgId, string assistantId, CancellationToken cancellationToken = default)
    {
        if (await _drafts.GetAsync(tenantId, orgId, assistantId, cancellationToken) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_not_found");
        var pointer = await _publications.GetPointerAsync(tenantId, orgId, assistantId, cancellationToken);
        var version = await GetCurrentAsync(tenantId, orgId, assistantId, cancellationToken);
        return new(pointer?.PointerRevision ?? 0, version is not null, pointer?.AssistantKey ?? assistantId,
            version?.VersionNo ?? 0, version?.PublishedAt);
    }

    private static AssistantPublishedVersionDto Map(AssistantPublishedVersion value)
        => new(
            value.Id,
            value.AssistantKey,
            value.AssistantId,
            value.TenantId,
            value.OrgId,
            value.VersionNo,
            value.Name,
            value.Description,
            value.SnapshotJson,
            value.SnapshotHash,
            value.IsRevoked ? AssistantPublicationStatus.Revoked : AssistantPublicationStatus.Active,
            value.PublishedAt,
            value.PublishedBy,
            value.RevokedAt,
            value.RevokedBy);

    private static void RequireActorAndScope(string actorUserId, string tenantId, string orgId, string assistantId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)
            || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(orgId)
            || string.IsNullOrWhiteSpace(assistantId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
    }
}
