#nullable enable
using SmartAssistant.Models.Knowledge;
using SmartAssistant.Models.Policies;

namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 发布或预览时冻结的模型策略、知识版本、工具和联网搜索配置。
/// </summary>
public sealed record AssistantExecutionSnapshot(
    int SchemaVersion,
    AssistantResolvedPolicy Policy,
    string? KnowledgeBaseId,
    IReadOnlyList<AssistantFrozenQuestion> Questions,
    IReadOnlyList<AssistantFrozenDocument> Documents,
    IReadOnlyList<AssistantFrozenTool>? Tools = null,
    AssistantKnowledgeEmbeddingExecutionProfile? KnowledgeEmbedding = null,
    AssistantWebSearchExecutionProfile? WebSearch = null);
