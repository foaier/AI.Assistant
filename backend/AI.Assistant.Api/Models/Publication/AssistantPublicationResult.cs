#nullable enable
namespace AI.Assistant.Models.Publication;

/// <summary>
/// 发布或撤回后的发布指针及版本结果。
/// </summary>
public sealed record AssistantPublicationResult(
    AssistantPublishedVersionDto Version,
    long PointerRevision,
    bool Changed);
