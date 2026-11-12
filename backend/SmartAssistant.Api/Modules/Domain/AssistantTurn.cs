#nullable enable
using System;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 代表一条用户请求对应的对话轮次，维护幂等内容与生命周期状态。
/// </summary>
public sealed class AssistantTurn
{
    private AssistantTurn() { }

    public string Id { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public string PublishedVersionId { get; private set; } = string.Empty;
    /// <summary>
    /// 客户端生成的消息幂等标识，在同一对话内用于识别重复提交。
    /// </summary>
    public string ClientMessageId { get; private set; } = string.Empty;
    /// <summary>
    /// 标准化请求内容的指纹，用于判断相同消息标识是否对应同一请求。
    /// </summary>
    public string RequestHash { get; private set; } = string.Empty;
    public long TurnNo { get; private set; }
    public AssistantExecutionMode ExecutionMode { get; private set; }
    public AssistantTurnStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }

    public static AssistantTurn Create(
        string id,
        string conversationId,
        string sessionId,
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string clientMessageId,
        string messageType,
        string text,
        string locale,
        long turnNo,
        AssistantExecutionMode executionMode,
        DateTime now)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, tenantId, orgId, publishedVersionId, executionMode);
        if (turnNo < 1)
            throw new BizResultException(BusinessCodes.BusinessException, "TurnNo must be positive");

        return new AssistantTurn
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            ConversationId = AssistantRuntimeGuard.Required(conversationId, "ConversationId"),
            SessionId = AssistantRuntimeGuard.Required(sessionId, "SessionId"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            TenantId = AssistantRuntimeGuard.Required(tenantId, "TenantId"),
            OrgId = AssistantRuntimeGuard.Required(orgId, "OrgId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            ClientMessageId = AssistantRuntimeGuard.Required(clientMessageId, "ClientMessageId", 160),
            RequestHash = AssistantRuntimeGuard.ComputeRequestHash(messageType, text, locale),
            TurnNo = turnNo,
            ExecutionMode = executionMode,
            Status = AssistantTurnStatus.Accepted,
            CreatedAt = now
        };
    }

    public static AssistantTurn Rehydrate(
        string id,
        string conversationId,
        string sessionId,
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        string clientMessageId,
        string requestHash,
        long turnNo,
        AssistantExecutionMode executionMode,
        AssistantTurnStatus status,
        DateTime createdAt,
        DateTime? completedAt,
        string? failureCode)
        => new()
        {
            Id = id,
            ConversationId = conversationId,
            SessionId = sessionId,
            AssistantId = assistantId,
            TenantId = tenantId,
            OrgId = orgId,
            PublishedVersionId = publishedVersionId,
            ClientMessageId = clientMessageId,
            RequestHash = requestHash,
            TurnNo = turnNo,
            ExecutionMode = executionMode,
            Status = status,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            FailureCode = failureCode
        };

    public void EnsureScope(AssistantScopeSnapshot scope, string conversationId, string sessionId)
    {
        if (!string.Equals(ConversationId, conversationId, StringComparison.Ordinal)
            || !string.Equals(SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_turn_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureScope(
            AssistantId, TenantId, OrgId, PublishedVersionId, ExecutionMode, scope);
    }

    public void Start(DateTime now)
    {
        if (Status != AssistantTurnStatus.Accepted)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_not_accepted");
        Status = AssistantTurnStatus.Running;
        CompletedAt = null;
        FailureCode = null;
    }

    public void Complete(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Completed, null, now);

    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Failed, failureCode, now);

    public void Cancel(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Cancelled, "cancelled", now);

    public void Expire(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Expired, "expired", now);

    private void CompleteTerminal(AssistantTurnStatus terminalStatus, string? failureCode, DateTime now)
    {
        if (Status is AssistantTurnStatus.Completed
            or AssistantTurnStatus.Failed
            or AssistantTurnStatus.Cancelled
            or AssistantTurnStatus.Expired)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_terminal_already_set");
        }

        if (Status != AssistantTurnStatus.Running && terminalStatus != AssistantTurnStatus.Cancelled)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_not_running");

        Status = terminalStatus;
        FailureCode = string.IsNullOrWhiteSpace(failureCode) ? null : failureCode.Trim();
        CompletedAt = now;
    }
}
