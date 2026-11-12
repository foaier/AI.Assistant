#nullable enable
using System;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Domain;

/// <summary>
/// 一次助手执行的领域状态，维护模型选择、事件序号、令牌用量和结束信息。
/// </summary>
public sealed class AssistantRun
{
    private AssistantRun() { }

    public string Id { get; private set; } = string.Empty;
    public string TurnId { get; private set; } = string.Empty;
    public string ConversationId { get; private set; } = string.Empty;
    public string SessionId { get; private set; } = string.Empty;
    public string AssistantId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string OrgId { get; private set; } = string.Empty;
    public string PublishedVersionId { get; private set; } = string.Empty;
    public AssistantExecutionMode ExecutionMode { get; private set; }
    public string ProviderProfileRef { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public AssistantRunStatus Status { get; private set; }
    /// <summary>
    /// 已分配的事件序号上界，由持久化层的序号预留操作独立推进。
    /// </summary>
    public long SequenceHead { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }
    public int? TotalTokens { get; private set; }

    public static AssistantRun Create(
        string id,
        AssistantTurn turn,
        string providerProfileRef,
        string model,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(turn);
        AssistantRuntimeGuard.RequireScope(
            turn.AssistantId,
            turn.TenantId,
            turn.OrgId,
            turn.PublishedVersionId,
            turn.ExecutionMode);

        return new AssistantRun
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            TurnId = turn.Id,
            ConversationId = turn.ConversationId,
            SessionId = turn.SessionId,
            AssistantId = turn.AssistantId,
            TenantId = turn.TenantId,
            OrgId = turn.OrgId,
            PublishedVersionId = turn.PublishedVersionId,
            ExecutionMode = turn.ExecutionMode,
            ProviderProfileRef = AssistantRuntimeGuard.Required(providerProfileRef, "ProviderProfileRef", 160),
            Model = AssistantRuntimeGuard.Required(model, "Model", 160),
            Status = AssistantRunStatus.Accepted,
            SequenceHead = 0,
            CreatedAt = now
        };
    }

    public static AssistantRun Rehydrate(
        string id,
        string turnId,
        string conversationId,
        string sessionId,
        string assistantId,
        string tenantId,
        string orgId,
        string publishedVersionId,
        AssistantExecutionMode executionMode,
        string providerProfileRef,
        string model,
        AssistantRunStatus status,
        long sequenceHead,
        DateTime createdAt,
        DateTime? startedAt,
        DateTime? completedAt,
        string? failureCode,
        int? promptTokens,
        int? completionTokens,
        int? totalTokens)
        => new()
        {
            Id = id,
            TurnId = turnId,
            ConversationId = conversationId,
            SessionId = sessionId,
            AssistantId = assistantId,
            TenantId = tenantId,
            OrgId = orgId,
            PublishedVersionId = publishedVersionId,
            ExecutionMode = executionMode,
            ProviderProfileRef = providerProfileRef,
            Model = model,
            Status = status,
            SequenceHead = sequenceHead,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            FailureCode = failureCode,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens
        };

    public void EnsureScope(AssistantScopeSnapshot scope, string turnId, string conversationId, string sessionId)
    {
        if (!string.Equals(TurnId, turnId, StringComparison.Ordinal)
            || !string.Equals(ConversationId, conversationId, StringComparison.Ordinal)
            || !string.Equals(SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_run_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureScope(
            AssistantId, TenantId, OrgId, PublishedVersionId, ExecutionMode, scope);
    }

    /// <summary>
    /// 在预期事件序号匹配时分配下一序号，并检查序号上限。
    /// </summary>
    public long ReserveNextSequence()
    {
        if (Status is AssistantRunStatus.Completed
            or AssistantRunStatus.Failed
            or AssistantRunStatus.Cancelled
            or AssistantRunStatus.Expired)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_terminal_already_set");
        }

        if (SequenceHead == long.MaxValue)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_sequence_exhausted");

        SequenceHead++;
        return SequenceHead;
    }

    public void Start(DateTime now)
    {
        if (Status != AssistantRunStatus.Accepted)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_not_accepted");
        Status = AssistantRunStatus.Running;
        StartedAt = now;
        CompletedAt = null;
        FailureCode = null;
    }

    /// <summary>
    /// 记录供应商返回的令牌用量，供运行结果与统计查询使用。
    /// </summary>
    public void RecordUsage(AssistantProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        PromptTokens = usage.PromptTokens;
        CompletionTokens = usage.CompletionTokens;
        TotalTokens = usage.TotalTokens;
    }

    public void Complete(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Completed, null, now);

    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantRunStatus.Failed, failureCode, now);

    public void Cancel(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Cancelled, "cancelled", now);

    public void Expire(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Expired, "expired", now);

    private void CompleteTerminal(AssistantRunStatus terminalStatus, string? failureCode, DateTime now)
    {
        if (Status is AssistantRunStatus.Completed
            or AssistantRunStatus.Failed
            or AssistantRunStatus.Cancelled
            or AssistantRunStatus.Expired)
        {
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_terminal_already_set");
        }

        if (Status != AssistantRunStatus.Running && terminalStatus != AssistantRunStatus.Cancelled)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_run_not_running");

        Status = terminalStatus;
        FailureCode = string.IsNullOrWhiteSpace(failureCode) ? null : failureCode.Trim();
        CompletedAt = now;
    }
}
