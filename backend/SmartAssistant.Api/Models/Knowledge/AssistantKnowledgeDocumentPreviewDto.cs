#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 文档预览结果，包含原文、结构块、分段和当前处理状态。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewDto(
    string DocumentId,
    string FileName,
    string ContentType,
    AssistantKnowledgeDocumentStatus Status,
    string OriginalText,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewBlock> Structure,
    IReadOnlyList<AssistantKnowledgeDocumentPreviewSegment> Segments);
