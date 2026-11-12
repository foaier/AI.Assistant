#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 用于验证知识片段召回效果的问题输入。
/// </summary>
public sealed record TestAssistantKnowledgeRetrievalCommand(string Question);
