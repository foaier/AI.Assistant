#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 文档处理请求的结果，关联文档状态与索引任务。
/// </summary>
public sealed record AssistantKnowledgeDocumentProcessResult(
    AssistantKnowledgeDocumentDto Document,
    AssistantKnowledgeIndexJobDto Job);
