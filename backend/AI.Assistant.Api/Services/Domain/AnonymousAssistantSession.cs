#nullable enable
using System.Security.Cryptography;
using System.Text;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 维护匿名访问主体、授权范围和有效期，并控制会话过期与撤销。
/// </summary>
public sealed class AnonymousAssistantSession
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AnonymousAssistantSession() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; private set; } = string.Empty;
    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; private set; } = string.Empty;
    /// <summary>
    /// 保存会话主体标识的摘要。
    /// </summary>
    public string SubjectHash { get; private set; } = string.Empty;
    /// <summary>
    /// 记录会话或请求使用的语言区域。
    /// </summary>
    public string Locale { get; private set; } = "zh-cn";
    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    public AssistantExecutionMode ExecutionMode { get; private set; }
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantSessionStatus Status { get; private set; }
    /// <summary>
    /// 记录匿名会话最近一次访问的时间。
    /// </summary>
    public DateTime LastSeenAt { get; private set; }
    /// <summary>
    /// 记录会话、票据或预览失效时间。
    /// </summary>
    public DateTime ExpiresAt { get; private set; }
    /// <summary>
    /// 记录授权或版本撤回的时间。
    /// </summary>
    public DateTime? RevokedAt { get; private set; }

    /// <summary>
    /// 将匿名访问主体转换为稳定哈希，避免持久化时直接保留原始主体标识。
    /// </summary>
    public static string HashSubject(string bearerToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AssistantRuntimeGuard.Required(bearerToken, "BearerToken", 256)))).ToLowerInvariant();

    /// <summary>
    /// 根据校验后的输入创建匿名助手会话领域对象，并初始化其状态和时间字段。
    /// </summary>
    public static AnonymousAssistantSession Create(
        string id,
        string assistantId,
        string publishedVersionId,
        string subjectHash,
        string locale,
        AssistantExecutionMode executionMode,
        DateTime now,
        DateTime expiresAt)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, publishedVersionId, executionMode);
        AssistantRuntimeGuard.EnsureTimeWindow(now, expiresAt);

        return new AnonymousAssistantSession
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            SubjectHash = AssistantRuntimeGuard.Required(subjectHash, "SubjectHash", 128),
            Locale = AssistantRuntimeGuard.NormalizeLocale(locale),
            ExecutionMode = executionMode,
            Status = AssistantSessionStatus.Active,
            LastSeenAt = now,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// 从持久化数据还原匿名助手会话的标识、访问范围和生命周期状态。
    /// </summary>
    public static AnonymousAssistantSession Rehydrate(
        string id,
        string assistantId,
        string publishedVersionId,
        string subjectHash,
        string locale,
        AssistantExecutionMode executionMode,
        AssistantSessionStatus status,
        DateTime lastSeenAt,
        DateTime expiresAt,
        DateTime? revokedAt)
        => new()
        {
            Id = id,
            AssistantId = assistantId,
            PublishedVersionId = publishedVersionId,
            SubjectHash = subjectHash,
            Locale = locale,
            ExecutionMode = executionMode,
            Status = status,
            LastSeenAt = lastSeenAt,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt
        };

    /// <summary>
    /// 校验会话状态、有效期和助手发布范围，拒绝跨范围或已失效的会话访问。
    /// </summary>
    public void EnsureAccessible(
        string assistantId,
        string publishedVersionId,
        AssistantExecutionMode executionMode,
        DateTime now)
    {
        if (!string.Equals(AssistantId, assistantId, StringComparison.Ordinal)
            || !string.Equals(PublishedVersionId, publishedVersionId, StringComparison.Ordinal)
            || ExecutionMode != executionMode)
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_session_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureActive(Status, now, ExpiresAt, "assistant_session_expired");
    }

    /// <summary>
    /// 更新当前对象的最近活动时间。
    /// </summary>
    public void Touch(DateTime now)
    {
        AssistantRuntimeGuard.EnsureActive(Status, now, ExpiresAt, "assistant_session_expired");
        LastSeenAt = now;
    }

    /// <summary>
    /// 将匿名助手会话标记为过期，并记录状态转换时间。
    /// </summary>
    public void Expire(DateTime now)
    {
        if (Status == AssistantSessionStatus.Revoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_revoked");
        if (Status == AssistantSessionStatus.Expired)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_terminal_already_set");
        Status = AssistantSessionStatus.Expired;
        LastSeenAt = now;
    }

    /// <summary>
    /// 撤回匿名助手会话的当前有效状态，并记录撤回时间。
    /// </summary>
    public void Revoke(DateTime now)
    {
        if (Status == AssistantSessionStatus.Revoked)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_session_terminal_already_set");
        Status = AssistantSessionStatus.Revoked;
        RevokedAt = now;
        LastSeenAt = now;
    }
}
