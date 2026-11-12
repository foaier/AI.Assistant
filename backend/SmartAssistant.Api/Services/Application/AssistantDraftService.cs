#nullable enable
using System.Text.Json;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Models.Assistants;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Events;
using SmartAssistant.Services.Contracts;
using SmartAssistant.Services.Domain;
using SmartAssistant.Services.Infrastructure;

namespace SmartAssistant.Services.Application;

/// <summary>
/// 管理助手草稿的创建、读取、保存和删除。
/// 创建时校验幂等请求，保存时校验修订号，并将领域事件与草稿变更一并提交。
/// </summary>
public sealed class AssistantDraftService : IAssistantDraftService
{
    /// <summary>
    /// 保存助手草稿仓储依赖，供助手草稿服务执行相关操作。
    /// </summary>
    private readonly IAssistantDraftRepository _repository;
    /// <summary>
    /// 保存助手发布仓储依赖，供助手草稿服务执行相关操作。
    /// </summary>
    private readonly IAssistantPublicationRepository _publications;
    /// <summary>
    /// 保存数据库上下文依赖，供助手草稿服务执行相关操作。
    /// </summary>
    private readonly IDbContext _dbContext;
    /// <summary>
    /// 保存事件依赖，供助手草稿服务执行相关操作。
    /// </summary>
    private readonly IDomainEventPublisher _events;
    /// <summary>
    /// 保存助手执行快照服务依赖，供助手草稿服务执行相关操作。
    /// </summary>
    private readonly AssistantExecutionSnapshotService _snapshots;

    /// <summary>
    /// 接收并保存助手草稿服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
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
    public async Task<AssistantDraftDto> CreateAsync(CreateAssistantDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new BizResultException(BusinessCodes.BusinessException, "command required");
        await _snapshots.ValidateBindingsAsync(command.DraftJson, cancellationToken);
        var key = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim();
        if (key is not null)
        {
            var existing = await _repository.GetByIdempotencyKeyAsync(key, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.Name, command.Name?.Trim(), StringComparison.Ordinal)
                    || !string.Equals(existing.Description, command.Description?.Trim(), StringComparison.Ordinal)
                    || !JsonEquivalent(existing.DraftJson, command.DraftJson))
                    throw new BizResultException(BusinessCodes.Conflict, "assistant_creation_idempotency_payload_conflict");
                return Map(existing);
            }
        }

        var draft = AssistantDraft.Create(Guid.NewGuid().ToString("N"), command.Name, command.Description, command.DraftJson, key);
        await _repository.AddAsync(draft, cancellationToken);
        await _events.PublishAsync(new AssistantDraftSavedEvent(draft.Id, draft.DraftRevision));
        await _dbContext.CommitAsync(cancellationToken);
        return Map(draft);
    }

    /// <summary>
    /// 读取助手列表。
    /// </summary>
    public async Task<IReadOnlyList<AssistantListItemDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var drafts = await _repository.ListAsync(cancellationToken);
        return drafts.Select(MapListItem).ToArray();
    }

    /// <summary>
    /// 创建草稿并转换为管理端使用的助手详情。
    /// </summary>
    public async Task<AssistantDto> CreateAssistantAsync(
        CreateAssistantDraftCommand command,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await CreateAsync(command, cancellationToken));

    /// <summary>
    /// 读取草稿并将配置转换为管理端的 JSON 对象。
    /// </summary>
    public async Task<AssistantDto> GetAssistantAsync(
        string assistantId,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await GetAsync(assistantId, cancellationToken));

    /// <summary>
    /// 保存草稿并返回更新后的管理端助手详情。
    /// </summary>
    public async Task<AssistantDto> SaveAssistantDraftAsync(
        string assistantId,
        SaveAssistantDraftCommand command,
        CancellationToken cancellationToken = default)
        => ToAssistantDto(await SaveAsync(assistantId, command, cancellationToken));

    /// <summary>
    /// 删除草稿前检查是否仍有有效发布，已发布助手必须先撤回。
    /// </summary>
    public async Task DeleteAssistantAsync(
        string assistantId,
        CancellationToken cancellationToken = default)
    {
        var draft = await _repository.GetAsync(assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        var pointer = await _publications.GetPointerAsync(assistantId, cancellationToken);
        if (pointer?.Status == AssistantPublicationPointerStatus.Active
            && pointer.CurrentPublishedVersionId is not null)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_published_revoke_before_delete");
        await _repository.DeleteAsync(assistantId, cancellationToken);
        await _dbContext.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定助手草稿；资源不存在时返回相应业务错误。
    /// </summary>
    public async Task<AssistantDraftDto> GetAsync(string assistantId, CancellationToken cancellationToken = default)
    {
        var draft = await _repository.GetAsync(assistantId, cancellationToken);
        return draft is null
            ? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found")
            : Map(draft);
    }

    /// <summary>
    /// 校验配置绑定与预期草稿修订号后保存变更，并记录草稿保存事件。
    /// </summary>
    public async Task<AssistantDraftDto> SaveAsync(string assistantId, SaveAssistantDraftCommand command, CancellationToken cancellationToken = default)
    {
        var draft = await _repository.GetAsync(assistantId, cancellationToken)
            ?? throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        await _snapshots.ValidateBindingsAsync(command.DraftJson, cancellationToken);
        draft.Save(command.Name, command.Description, command.DraftJson, command.ExpectedDraftRevision);
        try
        {
            await _repository.SaveAsync(draft, command.ExpectedDraftRevision, cancellationToken);
            await _events.PublishAsync(new AssistantDraftSavedEvent(draft.Id, draft.DraftRevision));
            await _dbContext.CommitAsync(cancellationToken);
        }
        catch (AssistantDraftRepository.DraftRevisionConflictException)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_draft_revision_conflict");
        }
        return Map(draft);
    }

    /// <summary>
    /// 将领域对象转换为应用层返回的数据传输对象。
    /// </summary>
    private static AssistantDraftDto Map(AssistantDraft draft) => new(
        draft.Id, draft.Name, draft.Description, draft.DraftJson,
        draft.DraftRevision);

    /// <summary>
    /// 将助手草稿映射为列表项，只保留列表展示和并发控制所需的信息。
    /// </summary>
    private static AssistantListItemDto MapListItem(AssistantDraft draft) => new(
        draft.Id, draft.Name, draft.Description, draft.DraftRevision);

    /// <summary>
    /// 解析草稿配置 JSON，并组合为管理端使用的助手详情对象。
    /// </summary>
    private static AssistantDto ToAssistantDto(AssistantDraftDto dto)
    {
        using var document = JsonDocument.Parse(dto.DraftJson);
        return new AssistantDto(
            dto.Id,
            dto.Name,
            dto.Description,
            dto.DraftRevision,
            document.RootElement.Clone());
    }

    /// <summary>
    /// 将配置文本解析并重新序列化为规范 JSON；无效 JSON 按业务校验错误拒绝。
    /// </summary>
    private static string NormalizeJson(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        using var document = JsonDocument.Parse(normalized);
        return JsonSerializer.Serialize(document.RootElement);
    }

    /// <summary>
    /// 按 JSON 结构比较两个配置，而不是比较不相关的空白与属性格式。
    /// </summary>
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
