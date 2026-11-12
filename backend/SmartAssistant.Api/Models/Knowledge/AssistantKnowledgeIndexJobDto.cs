#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 索引任务的管理信息，用于追踪处理状态与失败原因。
/// </summary>
public sealed record AssistantKnowledgeIndexJobDto(
    string Id,
    string DocumentId,
    int DocumentVersionNo,
    AssistantKnowledgeIndexJobStatus Status,
    int AttemptCount,
    int ChunkCount,
    string? ErrorCode,
    DateTime? CompletedAt,
    long? Generation = null,
    string? EmbeddingModel = null,
    int? EmbeddingDimension = null);
