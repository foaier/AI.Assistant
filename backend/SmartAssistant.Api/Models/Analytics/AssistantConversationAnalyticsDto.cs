#nullable enable
using SmartAssistant.Models.Runtime;

namespace SmartAssistant.Models.Analytics;

/// <summary>
/// 管理端会话分析列表中的单条会话信息。
/// </summary>
public sealed record AssistantConversationAnalyticsDto(
    string ConversationId,
    string Locale,
    AssistantConversationStatus Status,
    long LastTurnNo,
    DateTime LastActivityAt);
