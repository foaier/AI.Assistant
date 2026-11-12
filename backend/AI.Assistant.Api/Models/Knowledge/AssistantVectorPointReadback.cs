#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 向量写入后的回读结果，用于核对片段、内容哈希与索引点标识。
/// </summary>
public sealed record AssistantVectorPointReadback(
    string ChunkId,
    string ContentHash,
    string PointId);
