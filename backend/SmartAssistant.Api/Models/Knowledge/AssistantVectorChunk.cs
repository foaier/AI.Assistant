#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 待写入向量索引的文本片段及其文档版本和内容哈希。
/// </summary>
public sealed record AssistantVectorChunk(
    string ChunkId,
    string ContentHash,
    string DocumentId,
    int VersionNo,
    int ChunkIndex,
    string Content,
    float[] Vector);
