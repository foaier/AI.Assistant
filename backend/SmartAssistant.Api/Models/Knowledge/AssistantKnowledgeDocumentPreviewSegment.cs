#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 文档预览中的文本片段，记录顺序、来源标签和可用性。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewSegment(
    string Id,
    int Index,
    string Text,
    string SourceLabel,
    bool Available);
