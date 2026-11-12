#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识文档的管理信息，包含文件属性、版本与处理状态。
/// </summary>
public sealed record AssistantKnowledgeDocumentDto(
    string Id,
    string KnowledgeBaseId,
    string AssetId,
    string FileName,
    string ContentType,
    long SizeBytes,
    int CurrentVersionNo,
    AssistantKnowledgeDocumentStatus Status,
    int ChunkCount,
    AssistantKnowledgeIndexJobStatus? LastIndexJobStatus,
    string? LastErrorCode,
    string IndexStatus = "unknown",
    long? IndexGeneration = null);
