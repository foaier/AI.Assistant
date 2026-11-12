#nullable enable
using SmartAssistant.Models.Analytics;

namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 会话分析分页结果，包含列表和分页统计。
/// </summary>
public sealed record AssistantConversationsPage(
    IReadOnlyList<AssistantConversationAnalyticsDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
