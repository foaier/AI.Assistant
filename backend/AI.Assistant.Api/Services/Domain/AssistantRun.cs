#nullable enable
using AI.Assistant.Models.AI;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Platform;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 一次助手执行的领域状态，维护模型选择、事件序号、令牌用量和结束信息。
/// </summary>
public sealed class AssistantRun
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantRun() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此记录所属的用户请求轮次。
    /// </summary>
    public string TurnId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此消息、轮次或运行所属的对话。
    /// </summary>
    public string ConversationId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    public string SessionId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; private set; } = string.Empty;
    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; private set; } = string.Empty;
    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    public AssistantExecutionMode ExecutionMode { get; private set; }
    /// <summary>
    /// 保存本次执行使用的供应商配置及其修订引用。
    /// </summary>
    public string ProviderProfileRef { get; private set; } = string.Empty;
    /// <summary>
    /// 记录当前选择的模型标识。
    /// </summary>
    public string Model { get; private set; } = string.Empty;
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantRunStatus Status { get; private set; }
    /// <summary>
    /// 已分配的事件序号上界，由持久化层的序号预留操作独立推进。
    /// </summary>
    public long SequenceHead { get; private set; }
    /// <summary>
    /// 记录执行或索引任务的开始时间。
    /// </summary>
    public DateTime? StartedAt { get; private set; }
    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    public DateTime? CompletedAt { get; private set; }
    /// <summary>
    /// 保存运行或索引失败的稳定错误码。
    /// </summary>
    public string? FailureCode { get; private set; }
    /// <summary>
    /// 记录模型输入占用的令牌数量。
    /// </summary>
    public int? PromptTokens { get; private set; }
    /// <summary>
    /// 记录模型生成内容占用的令牌数量。
    /// </summary>
    public int? CompletionTokens { get; private set; }
    /// <summary>
    /// 记录本次模型调用消耗的令牌总数。
    /// </summary>
    public int? TotalTokens { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手执行领域对象，并初始化业务状态。
    /// </summary>
    public static AssistantRun Create(
        string id,
        AssistantTurn turn,
        string providerProfileRef,
        string model)
    {
        ArgumentNullException.ThrowIfNull(turn);
        AssistantRuntimeGuard.RequireScope(
            turn.AssistantId,
            turn.PublishedVersionId,
            turn.ExecutionMode);

        return new AssistantRun
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            TurnId = turn.Id,
            ConversationId = turn.ConversationId,
            SessionId = turn.SessionId,
            AssistantId = turn.AssistantId,
            PublishedVersionId = turn.PublishedVersionId,
            ExecutionMode = turn.ExecutionMode,
            ProviderProfileRef = AssistantRuntimeGuard.Required(providerProfileRef, "ProviderProfileRef", 160),
            Model = AssistantRuntimeGuard.Required(model, "Model", 160),
            Status = AssistantRunStatus.Accepted,
            SequenceHead = 0
        };
    }

    /// <summary>
    /// 从持久化数据还原助手执行的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantRun Rehydrate(
        string id,
        string turnId,
        string conversationId,
        string sessionId,
        string assistantId,
        string publishedVersionId,
        AssistantExecutionMode executionMode,
        string providerProfileRef,
        string model,
        AssistantRunStatus status,
        long sequenceHead,
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
            PublishedVersionId = publishedVersionId,
            ExecutionMode = executionMode,
            ProviderProfileRef = providerProfileRef,
            Model = model,
            Status = status,
            SequenceHead = sequenceHead,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            FailureCode = failureCode,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens
        };

    /// <summary>
    /// 确认资源的助手、发布版本和执行模式与请求上下文一致。
    /// </summary>
    public void EnsureScope(AssistantScopeSnapshot scope, string turnId, string conversationId, string sessionId)
    {
        if (!string.Equals(TurnId, turnId, StringComparison.Ordinal)
            || !string.Equals(ConversationId, conversationId, StringComparison.Ordinal)
            || !string.Equals(SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_run_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureScope(
            AssistantId, PublishedVersionId, ExecutionMode, scope);
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

    /// <summary>
    /// 将助手执行推进到执行中状态。
    /// </summary>
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

    /// <summary>
    /// 将助手执行标记为成功完成，并记录完成时间。
    /// </summary>
    public void Complete(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Completed, null, now);

    /// <summary>
    /// 将助手执行标记为失败，并保留失败状态供调用方查询。
    /// </summary>
    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantRunStatus.Failed, failureCode, now);

    /// <summary>
    /// 将当前对象标记为已取消，并记录取消终态。
    /// </summary>
    public void Cancel(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Cancelled, "cancelled", now);

    /// <summary>
    /// 将助手执行标记为过期，并记录状态转换时间。
    /// </summary>
    public void Expire(DateTime now)
        => CompleteTerminal(AssistantRunStatus.Expired, "expired", now);

    /// <summary>
    /// 将领域对象写入指定终态，并保存最终结果及状态转换时间。
    /// </summary>
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
