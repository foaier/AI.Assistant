#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 一批文本的向量生成结果，包含执行配置标识和向量维度。
/// </summary>
public sealed record AssistantEmbeddingResult(
    IReadOnlyList<float[]> Vectors,
    string ProfileId,
    int Dimension);
