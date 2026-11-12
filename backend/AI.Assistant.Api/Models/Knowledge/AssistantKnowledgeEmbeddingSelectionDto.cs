#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 选定的嵌入供应商、模型、维度及索引代次。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingSelectionDto(
    string ProviderId,
    long ProviderRevision,
    string Model,
    int Dimension,
    long Generation);
