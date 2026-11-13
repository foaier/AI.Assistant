#nullable enable
using AI.Assistant.Models.Analytics;
using AI.Assistant.Models.Runtime;
using AI.Assistant.Infrastructure;
using AI.Assistant.Services.Contracts;
using AI.Assistant.Services.Repositorys;

namespace AI.Assistant.Services.Application;

/// <summary>
/// 查询助手的会话、消息与运行统计，为管理端提供使用分析数据。
/// </summary>
public sealed class AssistantAnalyticsService : IAssistantAnalyticsService
{
    /// <summary>
    /// 保存助手分析仓储依赖，供助手分析服务执行相关操作。
    /// </summary>
    private readonly IAssistantAnalyticsRepository _repository;
    /// <summary>
    /// 保存助手草稿仓储依赖，供助手分析服务执行相关操作。
    /// </summary>
    private readonly IAssistantDraftRepository _drafts;
    /// <summary>
    /// 接收并保存助手分析服务执行所需的依赖，建立后续操作使用的运行状态。
    /// </summary>
    public AssistantAnalyticsService(IAssistantAnalyticsRepository repository, IAssistantDraftRepository drafts)
        => (_repository, _drafts) = (repository, drafts);

    /// <summary>
    /// 在指定助手范围内读取会话消息，供管理端查看会话内容。
    /// </summary>
    public async Task<AssistantMessagesPage> GetMessagesAsync(string assistantId, string conversationId, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        if (await _drafts.GetAsync(assistantId, cancellationToken) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        return await _repository.GetMessagesAsync(assistantId, conversationId, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// 分页读取助手的会话活动信息。
    /// </summary>
    public async Task<AssistantConversationsPage> ListConversationsAsync(string assistantId, int limit, int page, CancellationToken cancellationToken = default)
    {
        var result = await _repository.ListConversationsAsync(assistantId, limit, page, cancellationToken);
        var items = result.Items.Select(x => new AssistantConversationAnalyticsDto(x.Id, x.Locale,
            (AssistantConversationStatus)x.Status, x.LastTurnNo, x.LastActivityAt)).ToArray();
        return new(items, result.TotalCount, result.Page, result.PageSize);
    }

    /// <summary>
    /// 汇总助手的会话数量、轮次结果和回答来源等统计数据。
    /// </summary>
    public async Task<AssistantAnalyticsSummaryDto> GetSummaryAsync(string assistantId, CancellationToken cancellationToken = default)
    {
        var counts = await _repository.GetCountsAsync(assistantId, cancellationToken);
        return new AssistantAnalyticsSummaryDto(
            counts.Conversations,
            counts.Turns,
            counts.CompletedTurns,
            counts.FailedTurns,
            counts.Runs,
            counts.ToolCalls,
            counts.CustomQaAnswers,
            counts.KnowledgeAnswers,
            counts.ModelAnswers,
            counts.KnowledgeNoAnswers,
            counts.ToolCallsCompleted,
            counts.ToolCallsFailed,
            counts.ToolCallsDenied,
            counts.AverageToolLatencyMs);
    }
}
