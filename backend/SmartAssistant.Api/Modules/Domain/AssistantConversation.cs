#nullable enable
using System;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 维护匿名会话下的对话范围、状态和轮次序号。
/// </summary>
public sealed class AssistantConversation
{
    private AssistantConversation() { }

    public string Id { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public string PublishedVersionId { get; private set; } = string.Empty;
    public string Locale { get; private set; } = "zh-cn";
    public AssistantExecutionMode ExecutionMode { get; private set; }
    public AssistantConversationStatus Status { get; private set; }
    /// <summary>
    /// 已分配的最后轮次号，用于保护并发消息提交的顺序。
    /// </summary>
    public long LastTurnNo { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastActivityAt { get; private set; }

    public static AssistantConversation Create(
        string id,
        string assistantId,
        string sessionId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string locale,
        AssistantExecutionMode executionMode,
        DateTime now)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, tenantId, orgId, publishedVersionId, executionMode);

        return new AssistantConversation
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            SessionId = AssistantRuntimeGuard.Required(sessionId, "SessionId"),
            TenantId = AssistantRuntimeGuard.Required(tenantId, "TenantId"),
            OrgId = AssistantRuntimeGuard.Required(orgId, "OrgId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            Locale = AssistantRuntimeGuard.NormalizeLocale(locale),
            ExecutionMode = executionMode,
            Status = AssistantConversationStatus.Active,
            LastTurnNo = 0,
            CreatedAt = now,
            LastActivityAt = now
        };
    }

    public static AssistantConversation Rehydrate(
        string id,
        string assistantId,
        string sessionId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string locale,
        AssistantExecutionMode executionMode,
        AssistantConversationStatus status,
        long lastTurnNo,
        DateTime createdAt,
        DateTime lastActivityAt)
        => new()
        {
            Id = id,
            AssistantId = assistantId,
            SessionId = sessionId,
            TenantId = tenantId,
            OrgId = orgId,
            PublishedVersionId = publishedVersionId,
            Locale = locale,
            ExecutionMode = executionMode,
            Status = status,
            LastTurnNo = lastTurnNo,
            CreatedAt = createdAt,
            LastActivityAt = lastActivityAt
        };

    /// <summary>
    /// 检查对话是否属于指定执行范围和匿名会话。
    /// </summary>
    public void EnsureScope(AssistantScopeSnapshot scope, string sessionId)
    {
        if (!string.Equals(SessionId, sessionId, StringComparison.Ordinal))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_conversation_session_mismatch");

        AssistantRuntimeGuard.EnsureScope(
            AssistantId, TenantId, OrgId, PublishedVersionId, ExecutionMode, scope);
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_conversation_not_active");
    }

    /// <summary>
    /// 在预期轮次号匹配时递增轮次进度，并检查序号上限。
    /// </summary>
    public long ReserveNextTurnNo(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_not_active");
        if (LastTurnNo == long.MaxValue)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_number_exhausted");
        LastTurnNo++;
        LastActivityAt = now;
        return LastTurnNo;
    }

    public void Touch(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_not_active");
        LastActivityAt = now;
    }

    public void Archive(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_terminal_already_set");
        Status = AssistantConversationStatus.Archived;
        LastActivityAt = now;
    }

    public void Expire(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_terminal_already_set");
        Status = AssistantConversationStatus.Expired;
        LastActivityAt = now;
    }
}
