#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 检索测试命中的文档片段，包含来源信息、正文和相关度。
/// </summary>
public sealed record AssistantKnowledgeRetrievalMatch(
    string SegmentId,
    string DocumentId,
    string DocumentName,
    int SegmentIndex,
    string Text,
    double Relevance);
