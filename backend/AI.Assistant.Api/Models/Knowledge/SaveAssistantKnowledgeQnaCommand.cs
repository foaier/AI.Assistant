#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 保存固定问答的内容、启用状态、排序和推荐设置。
/// </summary>
public sealed record SaveAssistantKnowledgeQnaCommand(
    string? Id,
    string Question,
    string? Answer,
    bool Enabled,
    int SortOrder,
    bool ShowAsSuggestion);
