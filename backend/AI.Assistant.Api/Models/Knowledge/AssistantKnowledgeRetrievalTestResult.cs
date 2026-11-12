#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 指定问题的知识检索测试结果。
/// </summary>
public sealed record AssistantKnowledgeRetrievalTestResult(
    string Question,
    IReadOnlyList<AssistantKnowledgeRetrievalMatch> Matches);
