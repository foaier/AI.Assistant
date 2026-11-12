#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 对话消息分页结果及继续查询所需的游标。
/// </summary>
public sealed record AssistantMessagesPage(
    IReadOnlyList<AssistantMessageSnapshot> Items,
    string? NextCursor,
    bool HasMore);
