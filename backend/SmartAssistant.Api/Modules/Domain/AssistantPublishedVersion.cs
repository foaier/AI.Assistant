#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 冻结的助手发布快照，保留版本号、内容哈希和发布、撤回审计信息。
/// </summary>
public sealed class AssistantPublishedVersion
{
    private AssistantPublishedVersion() { }

    public string Id { get; private set; } = string.Empty;
    public string AssistantKey { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public long VersionNo { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    /// <summary>
    /// 发布时冻结的配置与执行依赖，后续草稿修改不会覆盖此快照。
    /// </summary>
    public string SnapshotJson { get; private set; } = "{}";
    /// <summary>
    /// 发布快照的内容哈希，用于标识被冻结的配置内容。
    /// </summary>
    public string SnapshotHash { get; private set; } = string.Empty;
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public string PublishedBy { get; private set; } = string.Empty;

    public static AssistantPublishedVersion Create(
        AssistantDraft draft,
        long versionNo,
        string actorUserId,
        DateTime now,
        string executionSnapshotJson)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        Require(actorUserId, nameof(actorUserId));
        if (versionNo < 1) throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_version_invalid");

        var snapshotJson = NormalizeJson(executionSnapshotJson);
        return new AssistantPublishedVersion
        {
            Id = Guid.NewGuid().ToString("N"),
            AssistantKey = draft.Id,
            AssistantId = draft.Id,
            TenantId = draft.TenantId,
            OrgId = draft.OrgId,
            VersionNo = versionNo,
            Name = draft.Name,
            Description = draft.Description,
            SnapshotJson = snapshotJson,
            SnapshotHash = Hash(snapshotJson),
            PublishedAt = now,
            PublishedBy = actorUserId
        };
    }

    public static AssistantPublishedVersion Rehydrate(
        string id,
        string assistantKey,
        string assistantId,
        string tenantId,
        string orgId,
        long versionNo,
        string name,
        string? description,
        string snapshotJson,
        string snapshotHash,
        bool isRevoked,
        DateTime? revokedAt,
        string? revokedBy,
        bool isDeleted,
        DateTime? deletedAt,
        string? deletedBy,
        DateTime publishedAt,
        string publishedBy)
        => new()
        {
            Id = id,
            AssistantKey = assistantKey,
            AssistantId = assistantId,
            TenantId = tenantId,
            OrgId = orgId,
            VersionNo = versionNo,
            Name = name,
            Description = description,
            SnapshotJson = snapshotJson,
            SnapshotHash = snapshotHash,
            IsRevoked = isRevoked,
            RevokedAt = revokedAt,
            RevokedBy = revokedBy,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy,
            PublishedAt = publishedAt,
            PublishedBy = publishedBy
        };

    /// <summary>
    /// 记录发布版本的撤回状态及审计信息。
    /// </summary>
    public void Revoke(string actorUserId, DateTime now)
    {
        Require(actorUserId, nameof(actorUserId));
        if (IsDeleted || IsRevoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_publication_already_revoked");
        IsRevoked = true;
        RevokedAt = now;
        RevokedBy = actorUserId;
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
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_publication_snapshot_invalid_json");
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BizResultException(BusinessCodes.BusinessException, $"{field} required")
            : value.Trim();
}
