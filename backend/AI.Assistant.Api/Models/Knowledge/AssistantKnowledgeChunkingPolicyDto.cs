#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 管理端显示的分块策略及其修订号、待处理文档状态。
/// </summary>
public sealed record AssistantKnowledgeChunkingPolicyDto(
    AssistantKnowledgeChunkingMode Mode,
    int MaxChunkSize,
    int MinChunkSize,
    int OverlapSize,
    int MaxHeadingLevel,
    bool PreserveHeadingPath,
    bool MergeShortChunks,
    long Revision,
    bool HasPendingDocuments);
