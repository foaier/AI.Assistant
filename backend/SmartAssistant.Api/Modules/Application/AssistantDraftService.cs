#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
/// 管理助手草稿的创建、读取、保存和删除。
/// 创建时校验幂等请求，保存时校验修订号，并将领域事件与草稿变更一并提交。
/// </summary>
public sealed class AssistantDraftService : IAssistantDraftService
{
    private readonly IAssistantDraftRepository _repository;
    private readonly IAssistantPublicationRepository _publications;
    private readonly IDbContext _dbContext;
    private readonly IDomainEventPublisher _events;
    private readonly AssistantExecutionSnapshotService _snapshots;

    public AssistantDraftService(IAssistantDraftRepository repository, IAssistantPublicationRepository publications,
        IDbContext dbContext, IDomainEventPublisher events,
        AssistantExecutionSnapshotService snapshots)
    {
        _repository = repository;
        _publications = publications;
        _dbContext = dbContext;
        _events = events;
        _snapshots = snapshots;
    }

    /// <summary>
    /// 校验配置与幂等键后创建助手草稿，重复的相同请求复用已有结果。
    /// </summary>
    public async Task<AssistantDraftDto> CreateAsync(string actorUserId, string tenantId, string orgId,
        CreateAssistantDraftCommand command, CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId);
        if (command is null) throw new BizResultException(BusinessCodes.BusinessException, "command required");
        await _snapshots.ValidateBindingsAsync(tenantId, orgId, command.DraftJson, cancellationToken);
        var key = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim();
        if (key is not null)
        {
            var existing = await _repository.GetByIdempotencyKeyAsync(tenantId, orgId, key, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.Name, command.Name?.Trim(), StringComparison.Ordinal)
                    || !string.Equals(existing.Description, command.Description?.Trim(), StringComparison.Ordinal)
                    || !JsonEquivalent(existing.DraftJson, command.DraftJson))
                    throw new BizResultException(BusinessCodes.Conflict, "assistant_creation_idempotency_payload_conflict");
                return Map(existing);
            }
        }

        var draft = AssistantDraft.Create(Guid.NewGuid().ToString("N"), tenantId, orgId,
            command.Name, command.Description, command.DraftJson, actorUserId, key, DateTime.UtcNow);
        await _repository.AddAsync(draft, cancellationToken);
        await _events.PublishAsync(new AssistantDraftSavedEvent(draft.Id, draft.TenantId, draft.OrgId, draft.DraftRevision, actorUserId));
        await _dbContext.CommitAsync(cancellationToken);
        return Map(draft);
    }

    /// <summary>
    /// 读取当前租户和组织范围内的助手列表。
    /// </summary>
    public async Task<IReadOnlyList<AssistantListItemDto>> ListAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId);
        var drafts = await _repository.ListAsync(tenantId, orgId, cancellationToken);
        return drafts.Select(MapListItem).ToArray();
    }

    /// <summary>
    /// 创建草稿并转换为管理端使用的助手详情。
    /// </summary>
    public async Task<AssistantDto> CreateAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await CreateAsync(actorUserId, tenantId, orgId, command, cancellationToken));

    /// <summary>
    /// 读取草稿并将配置转换为管理端的 JSON 对象。
    /// </summary>
    public async Task<AssistantDto> GetAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await GetAsync(actorUserId, tenantId, orgId, assistantId, cancellationToken));

    /// <summary>
    /// 保存草稿并返回更新后的管理端助手详情。
    /// </summary>
    public async Task<AssistantDto> SaveAssistantDraftAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await SaveAsync(actorUserId, tenantId, orgId, assistantId, command, cancellationToken));

    /// <summary>
    /// 删除草稿前检查是否仍有有效发布，已发布助手必须先撤回。
    /// </summary>
    public async Task DeleteAssistantAsync(
        string actorUserId,
        string tenantId,
        string orgId,
        string assistantId,
        CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId);
        var draft = await _repository.GetAsync(tenantId, orgId, assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        var pointer = await _publications.GetPointerAsync(tenantId, orgId, assistantId, cancellationToken);
        if (pointer?.Status == AssistantPublicationPointerStatus.Active
            && pointer.CurrentPublishedVersionId is not null)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_published_revoke_before_delete");
        draft.Delete(actorUserId, DateTime.UtcNow);
        await _repository.DeleteAsync(tenantId, orgId, assistantId, actorUserId, draft.UpdatedAt, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定助手草稿；资源不存在时返回相应业务错误。
    /// </summary>
    public async Task<AssistantDraftDto> GetAsync(string actorUserId, string tenantId, string orgId,
        string assistantId, CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId);
        var draft = await _repository.GetAsync(tenantId, orgId, assistantId, cancellationToken);
        return draft is null
            ? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found")
            : Map(draft);
    }

    /// <summary>
    /// 校验配置绑定与预期草稿修订号后保存变更，并记录草稿保存事件。
    /// </summary>
    public async Task<AssistantDraftDto> SaveAsync(string actorUserId, string tenantId, string orgId,
        string assistantId, SaveAssistantDraftCommand command, CancellationToken cancellationToken = default)
    {
        RequireActorAndScope(actorUserId, tenantId, orgId);
        var draft = await _repository.GetAsync(tenantId, orgId, assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        await _snapshots.ValidateBindingsAsync(tenantId, orgId, command.DraftJson, cancellationToken);
        draft.Save(command.Name, command.Description, command.DraftJson, command.ExpectedDraftRevision, actorUserId, DateTime.UtcNow);
        try
        {
            await _repository.SaveAsync(draft, command.ExpectedDraftRevision, cancellationToken);
            await _events.PublishAsync(new AssistantDraftSavedEvent(draft.Id, draft.TenantId, draft.OrgId, draft.DraftRevision, actorUserId));
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (AssistantDraftRepository.DraftRevisionConflictException)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_draft_revision_conflict");
        }
        return Map(draft);
    }

    private static void RequireActorAndScope(string actorUserId, string tenantId, string orgId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(orgId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
    }

    private static AssistantDraftDto Map(AssistantDraft draft) => new(
        draft.Id, draft.TenantId, draft.OrgId, draft.Name, draft.Description, draft.DraftJson,
        draft.DraftRevision, draft.UpdatedAt, draft.UpdatedBy);

    private static AssistantListItemDto MapListItem(AssistantDraft draft) => new(
        draft.Id, draft.Name, draft.Description, draft.DraftRevision, draft.UpdatedAt, draft.UpdatedBy);

    private static AssistantDto ToAssistantDto(AssistantDraftDto dto)
    {
        using var document = JsonDocument.Parse(dto.DraftJson);
        return new AssistantDto(
            dto.Id,
            dto.Name,
            dto.Description,
            dto.DraftRevision,
            dto.UpdatedAt,
            dto.UpdatedBy,
            document.RootElement.Clone());
    }

    private static string NormalizeJson(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        using var document = JsonDocument.Parse(normalized);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static bool JsonEquivalent(string? left, string? right)
    {
        try
        {
            using var leftDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(left) ? "{}" : left);
            using var rightDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(right) ? "{}" : right);
            return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
