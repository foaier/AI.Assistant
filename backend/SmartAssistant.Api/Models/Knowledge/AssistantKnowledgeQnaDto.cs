#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识库固定问答条目，包含启用状态、排序和推荐问题设置。
/// </summary>
public sealed record AssistantKnowledgeQnaDto(
    string Id,
    string KnowledgeBaseId,
    string Question,
    string? Answer,
    bool Enabled,
    int SortOrder,
    bool ShowAsSuggestion);
