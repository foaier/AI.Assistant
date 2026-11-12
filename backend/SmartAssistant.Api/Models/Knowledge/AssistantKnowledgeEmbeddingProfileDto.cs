#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识库嵌入配置状态，区分目标配置与已完成索引的生效配置。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingProfileDto(
    long Revision,
    string Status,
    AssistantKnowledgeEmbeddingSelectionDto? Target,
    AssistantKnowledgeEmbeddingSelectionDto? Active,
    int DocumentCount,
    int TargetReadyDocumentCount,
    string? LastErrorCode,
    IReadOnlyList<AssistantKnowledgeEmbeddingModelOptionDto> AvailableModels,
    bool ActiveAvailable = false);
