#nullable enable
namespace AI.Assistant.Models.Analytics;

/// <summary>
/// 数据库会话统计查询的分页结果。
/// </summary>
public sealed record AssistantAnalyticsConversationsPage(
    IReadOnlyList<AssistantAnalyticsConversationRow> Items,
    int TotalCount,
    int Page,
    int PageSize);
