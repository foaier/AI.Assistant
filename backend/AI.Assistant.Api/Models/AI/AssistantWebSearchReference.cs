#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 联网搜索返回的来源引用，供回答展示和证据关联使用。
/// </summary>
public sealed record AssistantWebSearchReference(
    string Title,
    string Url,
    string Excerpt);
