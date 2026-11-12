#nullable enable
namespace SmartAssistant.Models.Analytics;

/// <summary>
/// 会话统计查询返回的数据库投影。
/// </summary>
public sealed record AssistantAnalyticsConversationRow(
    string Id,
    string Locale,
    int Status,
    long LastTurnNo,
    DateTime LastActivityAt);
