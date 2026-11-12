#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 冻结到执行快照中的固定问答，保留标准化问题和推荐问题标记。
/// </summary>
public sealed record AssistantFrozenQuestion(
    string Id,
    string Question,
    string NormalizedQuestion,
    string? Answer,
    bool ShowAsSuggestion = false);
