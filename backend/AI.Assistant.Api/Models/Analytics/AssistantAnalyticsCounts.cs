#nullable enable
namespace AI.Assistant.Models.Analytics;

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
