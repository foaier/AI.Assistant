#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 创建知识库所需的名称和描述。
/// </summary>
public sealed record CreateAssistantKnowledgeBaseCommand(
    string Name,
    string? Description);
