#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 可供知识库选择的嵌入模型及其供应商修订和向量维度。
/// </summary>
public sealed record AssistantKnowledgeEmbeddingModelOptionDto(
    string ProviderId,
    long ProviderRevision,
    string ProviderName,
    string Model,
    int Dimension);
