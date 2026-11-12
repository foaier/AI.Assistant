#nullable enable
using AI.Assistant.Models.Runtime;

namespace AI.Assistant.Models.Preview;

/// <summary>
/// 预览消息列表与运行状态，包含可展示的失败码。
/// </summary>
public sealed record AssistantPreviewMessages(
    IReadOnlyList<AssistantMessageSnapshot> Items,
    bool Running,
    string? FailureCode);
