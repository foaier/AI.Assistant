#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 预览文档按当前策略切分后的片段及预计片段数量。
/// </summary>
public sealed record AssistantKnowledgeChunkPreviewDto(
    string DocumentId,
    int EstimatedChunkCount,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewSegment> Segments);
