#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 文档预览中的结构块，记录块类型、文本和标题层级。
/// </summary>
public sealed record AssistantKnowledgeDocumentPreviewBlock(
    string Kind,
    string Text,
    int Level);
