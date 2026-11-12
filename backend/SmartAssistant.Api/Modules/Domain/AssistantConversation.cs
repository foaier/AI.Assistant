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
    /// <summary>
    /// 限制领域对象直接构造；实例应通过校验过的工厂方法创建或由持久化层还原。
    /// </summary>
    private AssistantConversation() { }

    /// <summary>
    /// 记录当前数据的唯一标识。
    /// </summary>
    public string Id { get; private set; } = string.Empty;
    /// <summary>
    /// 关联此数据所属的助手。
    /// </summary>
    public string AssistantId { get; private set; } = string.Empty;
    /// <summary>
    /// 关联创建该对话或请求的匿名会话。
    /// </summary>
    public string SessionId { get; private set; } = string.Empty;
    /// <summary>
    /// 绑定创建此会话或运行时使用的固定发布版本。
    /// </summary>
    public string PublishedVersionId { get; private set; } = string.Empty;
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
    public AssistantConversationStatus Status { get; private set; }
    /// <summary>
    /// 已分配的最后轮次号，用于保护并发消息提交的顺序。
    /// </summary>
    public long LastTurnNo { get; private set; }
    /// <summary>
    /// 记录对话最近一次活动的时间。
    /// </summary>
    public DateTime LastActivityAt { get; private set; }

    /// <summary>
    /// 根据校验后的输入创建助手对话领域对象，并初始化其状态和时间字段。
    /// </summary>
    public static AssistantConversation Create(
        string id,
        string assistantId,
        string sessionId,
        string publishedVersionId,
        string locale,
        AssistantExecutionMode executionMode,
        DateTime now)
    {
        AssistantRuntimeGuard.RequireScope(
            assistantId, publishedVersionId, executionMode);

        return new AssistantConversation
        {
            Id = AssistantRuntimeGuard.Required(id, "Id"),
            AssistantId = AssistantRuntimeGuard.Required(assistantId, "AssistantId"),
            SessionId = AssistantRuntimeGuard.Required(sessionId, "SessionId"),
            PublishedVersionId = AssistantRuntimeGuard.Required(publishedVersionId, "PublishedVersionId"),
            Locale = AssistantRuntimeGuard.NormalizeLocale(locale),
            ExecutionMode = executionMode,
            Status = AssistantConversationStatus.Active,
            LastTurnNo = 0,
            LastActivityAt = now
        };
    }

    /// <summary>
    /// 从持久化数据还原助手对话的标识、访问范围和生命周期状态。
    /// </summary>
    public static AssistantConversation Rehydrate(
        string id,
        string assistantId,
        string sessionId,
        string publishedVersionId,
        string locale,
        AssistantExecutionMode executionMode,
        AssistantConversationStatus status,
        long lastTurnNo,
        DateTime lastActivityAt)
        => new()
        {
            Id = id,
            AssistantId = assistantId,
            SessionId = sessionId,
            PublishedVersionId = publishedVersionId,
            Locale = locale,
            ExecutionMode = executionMode,
            Status = status,
            LastTurnNo = lastTurnNo,
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
            AssistantId, PublishedVersionId, ExecutionMode, scope);
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

    /// <summary>
    /// 更新当前对象的最近活动时间。
    /// </summary>
    public void Touch(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_not_active");
        LastActivityAt = now;
    }

    /// <summary>
    /// 将助手对话标记为已归档，停止其后续活动处理。
    /// </summary>
    public void Archive(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_terminal_already_set");
        Status = AssistantConversationStatus.Archived;
        LastActivityAt = now;
    }

    /// <summary>
    /// 将助手对话标记为过期，并记录状态转换时间。
    /// </summary>
    public void Expire(DateTime now)
    {
        if (Status != AssistantConversationStatus.Active)
            throw new BizResultException(BusinessCodes.Conflict, "assistant_conversation_terminal_already_set");
        Status = AssistantConversationStatus.Expired;
        LastActivityAt = now;
    }
}
