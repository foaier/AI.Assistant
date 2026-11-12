#nullable enable
using System;
using System.Text.Json;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 可编辑的助手配置聚合，维护草稿修订号与创建、修改、删除审计信息。
/// </summary>
public sealed class AssistantDraft
{
    private AssistantDraft() { }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    /// <summary>
    /// 尚未发布的可编辑配置 JSON；公开运行使用独立冻结的发布快照。
    /// </summary>
    public string DraftJson { get; private set; } = "{}";
    /// <summary>
    /// 草稿的乐观并发修订号，每次成功保存后递增。
    /// </summary>
    public long DraftRevision { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;
    /// <summary>
    /// 客户端提供的创建幂等键，用于避免重复创建相同草稿。
    /// </summary>
    public string? CreationIdempotencyKey { get; private set; }

    public static AssistantDraft Create(
        string id, string tenantId, string orgId, string name, string? description,
        string draftJson, string actorUserId, string? idempotencyKey, DateTime now)
    {
        RequireScope(tenantId, orgId, actorUserId);
        var draft = new AssistantDraft
        {
            Id = Require(id, "Id"),
            TenantId = Require(tenantId, "TenantId"),
            OrgId = Require(orgId, "OrgId"),
            Name = NormalizeName(name),
            Description = description?.Trim(),
            DraftJson = NormalizeJson(draftJson),
            DraftRevision = 1,
            CreatedAt = now,
            CreatedBy = actorUserId,
            UpdatedAt = now,
            UpdatedBy = actorUserId,
            CreationIdempotencyKey = NormalizeOptional(idempotencyKey)
        };
        return draft;
    }

    public static AssistantDraft Rehydrate(
        string id, string tenantId, string orgId, string name, string? description,
        string draftJson, long revision, bool isDeleted, DateTime? deletedAt, string? deletedBy,
        DateTime createdAt, string createdBy, DateTime updatedAt, string updatedBy, string? idempotencyKey)
        => new()
        {
            Id = id,
            TenantId = tenantId,
            OrgId = orgId,
            Name = name,
            Description = description,
            DraftJson = draftJson,
            DraftRevision = revision,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy,
            CreationIdempotencyKey = idempotencyKey
        };

    /// <summary>
    /// 校验预期草稿修订号后更新配置和审计信息，并递增修订号。
    /// </summary>
    public void Save(string name, string? description, string draftJson, long expectedRevision, string actorUserId, DateTime now)
    {
        RequireScope(TenantId, OrgId, actorUserId);
        if (expectedRevision != DraftRevision)
            throw new BizResultException(BusinessCodes.Conflict, $"assistant_draft_revision_conflict:{DraftRevision}");
        Name = NormalizeName(name);
        Description = description?.Trim();
        DraftJson = NormalizeJson(draftJson);
        DraftRevision++;
        UpdatedAt = now;
        UpdatedBy = actorUserId;
    }

    /// <summary>
    /// 将草稿标记为已删除并记录操作人和时间；已删除草稿不能重复删除。
    /// </summary>
    public void Delete(string actorUserId, DateTime now)
    {
        RequireScope(TenantId, OrgId, actorUserId);
        if (IsDeleted)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_draft_not_found");
        IsDeleted = true;
        DeletedAt = now;
        DeletedBy = actorUserId;
        UpdatedAt = now;
        UpdatedBy = actorUserId;
    }

    private static void RequireScope(string tenantId, string orgId, string actorUserId)
    {
        _ = Require(tenantId, "TenantId");
        _ = Require(orgId, "OrgId");
        _ = Require(actorUserId, "ActorUserId");
    }

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();

    private static string NormalizeName(string? value)
    {
        var normalized = Require(value, "Name");
        if (normalized.Length > 120)
            throw new BizResultException(BusinessCodes.BusinessException, "Name too long");
        return normalized;
    }

    private static string NormalizeJson(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new BizResultException(BusinessCodes.BusinessException, "DraftJson must be valid JSON");
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
