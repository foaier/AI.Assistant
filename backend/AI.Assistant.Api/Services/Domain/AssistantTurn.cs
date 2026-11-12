#nullable enable
using AI.Assistant.Models.Runtime;
using AI.Assistant.Platform;

namespace AI.Assistant.Services.Domain;

/// <summary>
/// 代表一条用户请求对应的对话轮次，维护幂等内容与生命周期状态。
/// </summary>
public sealed class AssistantTurn
{
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantTurn() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
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
    /// 客户端生成的消息幂等标识，在同一对话内用于识别重复提交。
    /// </summary>
    public string ClientMessageId { get; private set; } = string.Empty;
    /// <summary>
    /// 标准化请求内容的指纹，用于判断相同消息标识是否对应同一请求。
    /// </summary>
    public string RequestHash { get; private set; } = string.Empty;
    /// <summary>
    /// 记录用户请求在所属对话中的轮次编号。
    /// </summary>
    public long TurnNo { get; private set; }
    /// <summary>
    /// 区分公开执行和管理端预览执行。
    /// </summary>
    public AssistantExecutionMode ExecutionMode { get; private set; }
    /// <summary>
    /// 记录当前生命周期状态。
    /// </summary>
    public AssistantTurnStatus Status { get; private set; }
    /// <summary>
    /// 记录处理完成或进入终态的时间。
    /// </summary>
    public DateTime? CompletedAt { get; private set; }
    /// <summary>
    /// 保存运行或索引失败的稳定错误码。
    /// </summary>
    public string? FailureCode { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手轮次领域对象，并初始化业务状态。
    /// </summary>
    public static AssistantTurn Create(
        string id,
        string conversationId,
        string sessionId,
        string assistantId,
        string publishedVersionId,
        string clientMessageId,
        string messageType,
        string text,
        string locale,
        long turnNo,
        AssistantExecutionMode executionMode)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, publishedVersionId, executionMode);
        if (turnNo < 1)
            throw new BizResultException(BusinessCodes.BusinessException, "TurnNo must be positive");

        return new AssistantTurn
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            ConversationId = AssistantRuntimeGuard.Required(conversationId, "ConversationId"),
            SessionId = AssistantRuntimeGuard.Required(sessionId, "SessionId"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            ClientMessageId = AssistantRuntimeGuard.Required(clientMessageId, "ClientMessageId", 160),
            RequestHash = AssistantRuntimeGuard.ComputeRequestHash(messageType, text, locale),
            TurnNo = turnNo,
            ExecutionMode = executionMode,
            Status = AssistantTurnStatus.Accepted
        };
    }

    /// <summary>
    /// 从持久化数据还原助手轮次的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantTurn Rehydrate(
        string id,
        string conversationId,
        string sessionId,
        string assistantId,
        string publishedVersionId,
        string clientMessageId,
        string requestHash,
        long turnNo,
        AssistantExecutionMode executionMode,
        AssistantTurnStatus status,
        DateTime? completedAt,
        string? failureCode)
        => new()
        {
            Id = id,
            ConversationId = conversationId,
            SessionId = sessionId,
            AssistantId = assistantId,
            PublishedVersionId = publishedVersionId,
            ClientMessageId = clientMessageId,
            RequestHash = requestHash,
            TurnNo = turnNo,
            ExecutionMode = executionMode,
            Status = status,
            CompletedAt = completedAt,
            FailureCode = failureCode
        };

    /// <summary>
    /// 确认资源的助手、发布版本和执行模式与请求上下文一致。
    /// </summary>
    public void EnsureScope(AssistantScopeSnapshot scope, string conversationId, string sessionId)
    {
        if (!string.Equals(ConversationId, conversationId, StringComparison.Ordinal)
            || !string.Equals(SessionId, sessionId, StringComparison.Ordinal))
        {
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_turn_scope_mismatch");
        }

        AssistantRuntimeGuard.EnsureScope(
            AssistantId, PublishedVersionId, ExecutionMode, scope);
    }

    /// <summary>
    /// 将助手轮次推进到执行中状态。
    /// </summary>
    public void Start()
    {
        if (Status != AssistantTurnStatus.Accepted)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_turn_not_accepted");
        Status = AssistantTurnStatus.Running;
        CompletedAt = null;
        FailureCode = null;
    }

    /// <summary>
    /// 将助手轮次标记为成功完成，并记录完成时间。
    /// </summary>
    public void Complete(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Completed, null, now);

    /// <summary>
    /// 将助手轮次标记为失败，并保留失败状态供调用方查询。
    /// </summary>
    public void Fail(string failureCode, DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Failed, failureCode, now);

    /// <summary>
    /// 将当前对象标记为已取消，并记录取消终态。
    /// </summary>
    public void Cancel(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Cancelled, "cancelled", now);

    /// <summary>
    /// 将助手轮次标记为过期，并记录状态转换时间。
    /// </summary>
    public void Expire(DateTime now)
        => CompleteTerminal(AssistantTurnStatus.Expired, "expired", now);

    /// <summary>
    /// 将领域对象写入指定终态，并保存最终结果及状态转换时间。
    /// </summary>
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
