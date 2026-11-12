#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 维护匿名访问主体、授权范围和有效期，并控制会话过期与撤销。
/// </summary>
public sealed class AnonymousAssistantSession
{
    private AnonymousAssistantSession() { }

    public string Id { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public string PublishedVersionId { get; private set; } = string.Empty;
    public string SubjectHash { get; private set; } = string.Empty;
    public string Locale { get; private set; } = "zh-cn";
    public AssistantExecutionMode ExecutionMode { get; private set; }
    public AssistantSessionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }

    /// <summary>
    /// 将匿名访问主体转换为稳定哈希，避免持久化时直接保留原始主体标识。
    /// </summary>
    public static string HashSubject(string bearerToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AssistantRuntimeGuard.Required(bearerToken, "BearerToken", 256)))).ToLowerInvariant();

    public static AnonymousAssistantSession Create(
        string id,
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string subjectHash,
        string locale,
        AssistantExecutionMode executionMode,
        DateTime now,
        DateTime expiresAt)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, tenantId, orgId, publishedVersionId, executionMode);
        AssistantRuntimeGuard.EnsureUtcWindow(now, expiresAt);

        return new AnonymousAssistantSession
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            TenantId = AssistantRuntimeGuard.Required(tenantId, "TenantId"),
            OrgId = AssistantRuntimeGuard.Required(orgId, "OrgId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            SubjectHash = AssistantRuntimeGuard.Required(subjectHash, "SubjectHash", 128),
            Locale = AssistantRuntimeGuard.NormalizeLocale(locale),
            ExecutionMode = executionMode,
            Status = AssistantSessionStatus.Active,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = expiresAt
        };
    }

    public static AnonymousAssistantSession Rehydrate(
        string id,
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string subjectHash,
        string locale,
        AssistantExecutionMode executionMode,
        AssistantSessionStatus status,
        DateTime createdAt,
        DateTime lastSeenAt,
        DateTime expiresAt,
        DateTime? revokedAt,
        string? revokedBy)
        => new()
        {
            Id = id,
            AssistantId = assistantId,
            TenantId = tenantId,
            OrgId = orgId,
            PublishedVersionId = publishedVersionId,
            SubjectHash = subjectHash,
            Locale = locale,
            ExecutionMode = executionMode,
            Status = status,
            CreatedAt = createdAt,
            LastSeenAt = lastSeenAt,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            RevokedBy = revokedBy
        };

    /// <summary>
    /// 校验会话状态、有效期和助手发布范围，拒绝跨范围或已失效的会话访问。
    /// </summary>
    public void EnsureAccessible(
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        AssistantExecutionMode executionMode,
        DateTime now)
    {
        if (!string.Equals(AssistantId, assistantId, StringComparison.Ordinal)
            || !string.Equals(TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(OrgId, orgId, StringComparison.Ordinal)
            || !string.Equals(PublishedVersionId, publishedVersionId, StringComparison.Ordinal)
            || ExecutionMode != executionMode)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureActive(Status, now, ExpiresAt, "assistant_session_expired");
    }

    public void Touch(DateTime now)
    {
        AssistantRuntimeGuard.EnsureActive(Status, now, ExpiresAt, "assistant_session_expired");
        LastSeenAt = now;
    }

    public void Expire(DateTime now)
    {
        if (Status == AssistantSessionStatus.Revoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_revoked");
        if (Status == AssistantSessionStatus.Expired)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_terminal_already_set");
        Status = AssistantSessionStatus.Expired;
        LastSeenAt = now;
    }

    public void Revoke(string actorId, DateTime now)
    {
        var actor = AssistantRuntimeGuard.Required(actorId, "ActorId");
        if (Status == AssistantSessionStatus.Revoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_terminal_already_set");
        Status = AssistantSessionStatus.Revoked;
        RevokedAt = now;
        RevokedBy = actor;
        LastSeenAt = now;
    }
}
