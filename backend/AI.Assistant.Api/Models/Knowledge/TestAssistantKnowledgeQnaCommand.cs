#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 用于验证固定问答匹配行为的问题输入。
/// </summary>
public sealed record TestAssistantKnowledgeQnaCommand(string Question);
