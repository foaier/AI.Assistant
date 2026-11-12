#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 运行时检索到的知识片段，包含文档来源、正文和相关度。
/// </summary>
public sealed record AssistantKnowledgeRuntimeMatch(
    string SegmentId,
    string DocumentId,
    string DocumentName,
    int SegmentIndex,
    string Text,
    double Relevance);
