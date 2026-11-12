#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 向量索引返回的候选片段及相似度和文档版本信息。
/// </summary>
public sealed record AssistantVectorSearchHit(
    string ChunkId,
    double Score,
    string? DocumentId,
    int? VersionNo,
    int? ChunkIndex);
