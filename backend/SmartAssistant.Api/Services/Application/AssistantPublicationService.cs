#nullable enable
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Models.Assistants;
using SmartAssistant.Models.Publication;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Events;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Infrastructure;

namespace SmartAssistant.Services.Application;

/// <summary>
/// 将草稿冻结为发布版本，并通过发布指针切换对外可用版本。
/// 发布与撤回均校验预期修订号，相关写入在同一事务内提交。
/// </summary>
public sealed class AssistantPublicationService : IAssistantPublicationService
{
    /// <summary>
    /// 保存助手草稿仓储依赖，供助手发布服务执行相关操作。
    /// </summary>
    private readonly IAssistantDraftRepository _drafts;
    /// <summary>
    /// 保存助手发布仓储依赖，供助手发布服务执行相关操作。
    /// </summary>
    private readonly IAssistantPublicationRepository _publications;
    /// <summary>
    /// 保存数据库上下文依赖，供助手发布服务执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;
    /// <summary>
    /// 保存事件依赖，供助手发布服务执行相关操作。
    /// </summary>
    private readonly IDomainEventPublisher _events;
    /// <summary>
    /// 保存助手执行快照服务依赖，供助手发布服务执行相关操作。
    /// </summary>
    private readonly AssistantExecutionSnapshotService _snapshots;

    /// <summary>
    /// 接收并保存助手发布服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
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
        string assistantId,
        PublishAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireAssistant(assistantId);
        if (command is null)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_command_required");

        var draft = await _drafts.GetAsync(assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        if (command.ExpectedDraftRevision != draft.DraftRevision)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_draft_revision_conflict");

        var pointer = await _publications.GetPointerAsync(assistantId, cancellationToken);
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
        var version = AssistantPublishedVersion.Create(draft, versionNo, now, snapshotJson);

        if (pointer?.CurrentPublishedVersionId is { } currentVersionId
            && pointer.Status == AssistantPublicationPointerStatus.Active)
        {
            var current = await _publications.GetVersionAsync(currentVersionId, cancellationToken);
            if (current is not null && !current.IsRevoked
                && string.Equals(current.SnapshotHash, version.SnapshotHash, StringComparison.Ordinal))
            {
                return new AssistantPublicationResult(Map(current), pointer.PointerRevision, false);
            }
        }

        await _publications.AddVersionAsync(version, cancellationToken);
        if (pointer is null)
        {
            pointer = AssistantPublicationPointer.Create(version);
            await _publications.AddPointerAsync(pointer, cancellationToken);
        }
        else
        {
            pointer.Advance(version, expectedPointerRevision!.Value);
            await _publications.SavePointerAsync(pointer, expectedPointerRevision.Value, cancellationToken);
        }

        await _events.PublishAsync(new AssistantPublishedEvent(
            version.AssistantId,
            version.Id,
            version.VersionNo));
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantPublicationResult(Map(version), pointer.PointerRevision, true);
    }

    /// <summary>
    /// 校验预期发布指针修订，撤回当前版本并记录撤回事件。
    /// </summary>
    public async Task<AssistantPublicationResult> RevokeAsync(
        string assistantId,
        RevokeAssistantPublicationCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireAssistant(assistantId);
        if (command is null)
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_command_required");

        var pointer = await _publications.GetPointerAsync(assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");
        var currentVersionId = pointer.CurrentPublishedVersionId
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");
        var version = await _publications.GetVersionAsync(currentVersionId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_publication_not_found");

        var now = DateTime.UtcNow;
        var revokedVersionId = pointer.Revoke(command.ExpectedPointerRevision);
        if (!string.Equals(revokedVersionId, version.Id, StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_pointer_version_conflict");

        version.Revoke(now);
        await _publications.SaveVersionAsync(version, cancellationToken);
        await _publications.SavePointerAsync(pointer, command.ExpectedPointerRevision, cancellationToken);
        await _events.PublishAsync(new AssistantPublicationRevokedEvent(
            pointer.AssistantId,
            version.Id,
            pointer.PointerRevision));
        await _dbContext.CommitAsync(cancellationToken);
        return new AssistantPublicationResult(Map(version), pointer.PointerRevision, true);
    }

    /// <summary>
    /// 读取当前指针指向的有效发布版本；未发布或已撤回时返回空值。
    /// </summary>
    public async Task<AssistantPublishedVersionDto?> GetCurrentAsync(
        string assistantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assistantId))
            return null;
        var pointer = await _publications.GetPointerAsync(assistantId, cancellationToken);
        if (pointer is null || pointer.Status != AssistantPublicationPointerStatus.Active
            || pointer.CurrentPublishedVersionId is null)
            return null;
        var version = await _publications.GetVersionAsync(pointer.CurrentPublishedVersionId, cancellationToken);
        return version is null || version.IsRevoked ? null : Map(version);
    }

    /// <summary>
    /// 读取助手当前的发布状态、指针修订和公开访问标识。
    /// </summary>
    public async Task<AssistantPublicationState> GetStateAsync(string assistantId, CancellationToken cancellationToken = default)
    {
        if (await _drafts.GetAsync(assistantId, cancellationToken) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_not_found");
        var pointer = await _publications.GetPointerAsync(assistantId, cancellationToken);
        var version = await GetCurrentAsync(assistantId, cancellationToken);
        return new(pointer?.PointerRevision ?? 0, version is not null, pointer?.AssistantKey ?? assistantId,
            version?.VersionNo ?? 0, version?.PublishedAt);
    }

    /// <summary>
    /// 将领域对象转换为应用层返回的数据传输对象。
    /// </summary>
    private static AssistantPublishedVersionDto Map(AssistantPublishedVersion value)
        => new(
            value.Id,
            value.AssistantKey,
            value.AssistantId,
            value.VersionNo,
            value.Name,
            value.Description,
            value.SnapshotJson,
            value.SnapshotHash,
            value.IsRevoked ? AssistantPublicationStatus.Revoked : AssistantPublicationStatus.Active,
            value.PublishedAt,
            value.RevokedAt);

    /// <summary>
    /// 检查助手标识已提供，缺少时拒绝执行写操作。
    /// </summary>
    private static void RequireAssistant(string assistantId)
    {
        if (string.IsNullOrWhiteSpace(assistantId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
    }
}
