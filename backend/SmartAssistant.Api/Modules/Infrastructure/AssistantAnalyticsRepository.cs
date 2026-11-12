#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartAssistant.Extensions.DAL;
using SmartAssistant.Modules.Contracts;
using SqlSugar;
using SmartAssistant.Platform;
using static SmartAssistant.Modules.Infrastructure.AssistantRuntimeRepository;

namespace SmartAssistant.Modules.Infrastructure;

/// <summary>
/// 会话统计查询返回的数据库投影。
/// </summary>
public sealed record AssistantAnalyticsConversationRow(string Id, string Locale, int Status, long LastTurnNo, DateTime CreatedAt, DateTime LastActivityAt);
/// <summary>
/// 数据库会话统计查询的分页结果。
/// </summary>
public sealed record AssistantAnalyticsConversationsPage(IReadOnlyList<AssistantAnalyticsConversationRow> Items, int TotalCount, int Page, int PageSize);
/// <summary>
/// 会话、轮次与运行结果的数据库聚合计数。
/// </summary>
public sealed record AssistantAnalyticsCounts(
    int Conversations,
    int Turns,
    int CompletedTurns,
    int FailedTurns,
    int Runs,
    int ToolCalls,
    int CustomQaAnswers,
    int KnowledgeAnswers,
    int ModelAnswers,
    int KnowledgeNoAnswers,
    int ToolCallsCompleted,
    int ToolCallsFailed,
    int ToolCallsDenied,
    double? AverageToolLatencyMs);

/// <summary>
/// 定义会话消息、分页列表与运行计数的持久化查询边界。
/// </summary>
public interface IAssistantAnalyticsRepository
{
    Task<AssistantMessagesPage> GetMessagesAsync(string tenantId, string orgId, string assistantId, string conversationId, int limit, string? cursor, CancellationToken ct);
    Task<AssistantAnalyticsConversationsPage> ListConversationsAsync(string tenantId, string orgId, string assistantId, int limit, int page, CancellationToken ct);
    Task<AssistantAnalyticsCounts> GetCountsAsync(string tenantId, string orgId, string assistantId, CancellationToken ct);
}

/// <summary>
/// 在授权范围内查询会话与消息数据，并汇总助手使用统计。
/// </summary>
public sealed class AssistantAnalyticsRepository(IDbContext db) : IAssistantAnalyticsRepository
{
    public async Task<AssistantMessagesPage> GetMessagesAsync(string tenantId, string orgId, string assistantId,
        string conversationId, int limit, string? cursor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var offset = 0;
        if (cursor is not null && (!int.TryParse(cursor, out offset) || offset < 0))
            throw new BizResultException(BusinessCodes.BusinessException, "assistant_messages_cursor_invalid");
        var conversation = await Conversations(tenantId, orgId, assistantId).Where(c => c.Id == conversationId).FirstAsync();
        if (conversation is null) throw new BizResultException(BusinessCodes.NotFound, "assistant_conversation_not_found");
        var version = conversation.PublishedVersionId;
        var sessionId = conversation.SessionId;
        var size = Math.Clamp(limit, 1, 100);
        var rows = await db.GetSugarScope(ConnectionType.Master).Queryable<MessageRow>()
            .InnerJoin<TurnRow>((m, t) => m.TurnId == t.Id && m.ConversationId == t.ConversationId)
            .Where((m, t) => m.ConversationId == conversationId && !m.IsDeleted && !t.IsDeleted
                && (m.Role == 1 || m.Role == 2) && t.TenantId == tenantId && t.OrgId == orgId
                && t.AssistantId == assistantId && t.PublishedVersionId == version && t.SessionId == sessionId && t.ExecutionMode == 1
                && (m.RunId == null || SqlFunc.Subqueryable<RunRow>().Where(r => r.Id == m.RunId && r.TurnId == t.Id
                    && r.ConversationId == conversationId && r.SessionId == sessionId && r.AssistantId == assistantId
                    && r.TenantId == tenantId && r.OrgId == orgId && r.PublishedVersionId == version && r.ExecutionMode == 1 && !r.IsDeleted).Any()))
            .OrderBy((m, t) => t.TurnNo).OrderBy((m, t) => m.CreatedAt).OrderBy((m, t) => m.Role).OrderBy((m, t) => m.Id)
            .Select((m, t) => m).Skip(offset).Take(size + 1).ToListAsync();
        var items = rows.Take(size).Select(m => new AssistantMessageSnapshot(m.Id, m.ConversationId, m.TurnId, m.RunId,
            (AssistantMessageRole)m.Role, (AssistantMessageStatus)m.Status, PublicBody(m.ContentJson), m.CreatedAt, m.CompletedAt)).ToArray();
        return new(items, rows.Count > size ? (offset + size).ToString(System.Globalization.CultureInfo.InvariantCulture) : null, rows.Count > size);
    }

    private static string PublicBody(string json)
    {
        try
        {
            using var body = JsonDocument.Parse(json);
            var text = body.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "";
            var source = body.RootElement.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            return JsonSerializer.Serialize(new { text, source });
        }
        catch (JsonException) { return "{}"; }
    }

    private ISugarQueryable<ConversationRow> Conversations(string tenant, string org, string assistant)
        => db.GetSugarScope(ConnectionType.Master).Queryable<ConversationRow>()
            .Where(c => c.TenantId == tenant && c.OrgId == org && c.AssistantId == assistant && !c.IsDeleted && c.ExecutionMode == 1
                && SqlFunc.Subqueryable<SessionRow>().Where(s => s.Id == c.SessionId && s.TenantId == tenant
                    && s.OrgId == org && s.AssistantId == assistant && s.PublishedVersionId == c.PublishedVersionId
                    && !s.IsDeleted && s.ExecutionMode == 1).Any());

    public async Task<AssistantAnalyticsConversationsPage> ListConversationsAsync(string tenantId, string orgId, string assistantId, int limit, int page, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var size = Math.Clamp(limit, 1, 100);
        var pageNumber = Math.Max(page, 1);
        var offset = (long)(pageNumber - 1) * size;
        var totalCount = await Conversations(tenantId, orgId, assistantId).CountAsync();
        if (offset >= totalCount)
            return new(Array.Empty<AssistantAnalyticsConversationRow>(), totalCount, pageNumber, size);

        var rows = await Conversations(tenantId, orgId, assistantId)
            .OrderBy(x => x.LastActivityAt, OrderByType.Desc)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .Skip((int)offset).Take(size).ToListAsync();
        var items = rows.Select(x => new AssistantAnalyticsConversationRow(x.Id, x.Locale, x.Status, x.LastTurnNo, x.CreatedAt, x.LastActivityAt)).ToArray();
        return new(items, totalCount, pageNumber, size);
    }

    public async Task<AssistantAnalyticsCounts> GetCountsAsync(string tenantId, string orgId, string assistantId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var conversations = await Conversations(tenantId, orgId, assistantId).ToListAsync();
        if (conversations.Count == 0) return EmptyCounts;
        var ids = conversations.Select(x => x.Id).ToArray();
        var scope = db.GetSugarScope(ConnectionType.Master);
        var turns = new List<TurnRow>();
        foreach (var id in ids)
        {
            var rows = await scope.Queryable<TurnRow>().Where(x => x.TenantId == tenantId && x.OrgId == orgId
                && x.AssistantId == assistantId && !x.IsDeleted && x.ExecutionMode == 1
                && x.ConversationId == id).ToListAsync();
            turns.AddRange(rows);
        }
        if (turns.Count == 0) return new(conversations.Count, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);
        var turnIds = turns.Select(x => x.Id).ToArray();
        var runs = new List<RunRow>();
        foreach (var id in turnIds)
        {
            var rows = await scope.Queryable<RunRow>().Where(x => x.TenantId == tenantId && x.OrgId == orgId
                && x.AssistantId == assistantId && !x.IsDeleted && x.ExecutionMode == 1
                && x.TurnId == id).ToListAsync();
            runs.AddRange(rows);
        }
        var runIds = runs.Select(x => x.Id).ToArray();
        var messages = new List<MessageRow>();
        foreach (var id in turnIds)
        {
            var rows = await scope.Queryable<MessageRow>().Where(x => !x.IsDeleted
                && x.Role == (int)AssistantMessageRole.Assistant && x.TurnId == id).ToListAsync();
            messages.AddRange(rows);
        }
        var sourceCounts = messages.Select(ReadSource).Where(x => x is not null)
            .GroupBy(x => x!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var tools = new List<ToolCallRow>();
        foreach (var id in runIds)
        {
            var rows = await scope.Queryable<ToolCallRow>().Where(x => !x.IsDeleted && x.RunId == id).ToListAsync();
            tools.AddRange(rows);
        }
        var completedTools = tools.Count(x => x.Status == (int)AssistantToolCallStatus.Completed);
        var failedTools = tools.Count(x => x.Status == (int)AssistantToolCallStatus.Failed);
        var deniedTools = tools.Count(x => x.Status == (int)AssistantToolCallStatus.Denied);
        var latencyValues = tools.Where(x => x.CompletedAt.HasValue)
            .Select(x => (x.CompletedAt!.Value - x.CreatedAt).TotalMilliseconds)
            .Where(x => x >= 0)
            .ToArray();
        return new(
            conversations.Count,
            turns.Count,
            turns.Count(x => x.Status == 3),
            turns.Count(x => x.Status == 4),
            runs.Count,
            tools.Count,
            Count(sourceCounts, "custom_qa"),
            Count(sourceCounts, "knowledge"),
            Count(sourceCounts, "model"),
            Count(sourceCounts, "knowledge_no_answer"),
            completedTools,
            failedTools,
            deniedTools,
            latencyValues.Length == 0 ? null : latencyValues.Average());
    }

    private static readonly AssistantAnalyticsCounts EmptyCounts =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

    private static int Count(IReadOnlyDictionary<string, int> counts, string key)
        => counts.TryGetValue(key, out var value) ? value : 0;

    private static string? ReadSource(MessageRow message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.ContentJson);
            return document.RootElement.TryGetProperty("source", out var source)
                && source.ValueKind == JsonValueKind.String
                ? source.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
