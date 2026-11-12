#nullable enable
namespace AI.Assistant.Models.Analytics;

/// <summary>
/// 助手会话与运行结果的汇总统计。
/// </summary>
public sealed record AssistantAnalyticsSummaryDto(
    int ConversationCount,
    int TurnCount,
    int CompletedTurnCount,
    int FailedTurnCount,
    int RunCount,
    int ToolCallCount,
    int CustomQaAnswerCount,
    int KnowledgeAnswerCount,
    int ModelAnswerCount,
    int KnowledgeNoAnswerCount,
    int ToolCallCompletedCount,
    int ToolCallFailedCount,
    int ToolCallDeniedCount,
    double? AverageToolLatencyMs);
