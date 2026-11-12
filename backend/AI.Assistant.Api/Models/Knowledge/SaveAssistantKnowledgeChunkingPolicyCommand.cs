#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 保存分块策略的命令，并指定是否重新处理已有文档。
/// </summary>
public sealed record SaveAssistantKnowledgeChunkingPolicyCommand(
    AssistantKnowledgeChunkingMode Mode,
    int MaxChunkSize,
    int MinChunkSize,
    int OverlapSize,
    int MaxHeadingLevel,
    bool PreserveHeadingPath,
    bool MergeShortChunks,
    bool ReprocessDocuments);
