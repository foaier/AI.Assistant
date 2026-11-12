#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 联网搜索的结构化结果及来源引用。
/// </summary>
public sealed record AssistantWebSearchResult(
    string Answer,
    IReadOnlyList<AssistantWebSearchReference> References);
