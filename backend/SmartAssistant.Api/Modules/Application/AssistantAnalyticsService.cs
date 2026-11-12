#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Modules.Infrastructure;
using SmartAssistant.Modules.Contracts;
using SmartAssistant.Platform;

namespace SmartAssistant.Modules.Application;

/// <summary>
/// 查询助手的会话、消息与运行统计，为管理端提供使用分析数据。
/// </summary>
public sealed class AssistantAnalyticsService : IAssistantAnalyticsService
{
    private readonly IAssistantAnalyticsRepository _repository;
    private readonly IAssistantDraftRepository _drafts;
    public AssistantAnalyticsService(IAssistantAnalyticsRepository repository, IAssistantDraftRepository drafts)
        => (_repository, _drafts) = (repository, drafts);

    /// <summary>
    /// 在指定助手范围内读取会话消息，供管理端查看会话内容。
    /// </summary>
    public async Task<AssistantMessagesPage> GetMessagesAsync(string actorUserId, string tenantId, string orgId,
        string assistantId, string conversationId, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(orgId))
            throw new BizResultException(BusinessCodes.Forbidden, "assistant_scope_required");
        if (await _drafts.GetAsync(tenantId, orgId, assistantId, cancellationToken) is null)
            throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        return await _repository.GetMessagesAsync(tenantId, orgId, assistantId, conversationId, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// 分页读取助手的会话活动信息。
    /// </summary>
    public async Task<AssistantConversationsPage> ListConversationsAsync(string actorUserId, string tenantId, string orgId, string assistantId, int limit, int page, CancellationToken cancellationToken = default)
    {
        var result = await _repository.ListConversationsAsync(tenantId, orgId, assistantId, limit, page, cancellationToken);
        var items = result.Items.Select(x => new AssistantConversationAnalyticsDto(x.Id, x.Locale,
            (AssistantConversationStatus)x.Status, x.LastTurnNo, x.CreatedAt, x.LastActivityAt)).ToArray();
        return new(items, result.TotalCount, result.Page, result.PageSize);
    }

    /// <summary>
    /// 汇总助手的会话数量、轮次结果和回答来源等统计数据。
    /// </summary>
    public async Task<AssistantAnalyticsSummaryDto> GetSummaryAsync(string actorUserId, string tenantId, string orgId, string assistantId, CancellationToken cancellationToken = default)
    {
        var counts = await _repository.GetCountsAsync(tenantId, orgId, assistantId, cancellationToken);
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
