#nullable enable
namespace AI.Assistant.Models.Assistants;

/// <summary>
/// 发布草稿的并发前提，要求草稿和发布指针都匹配预期修订号。
/// </summary>
public sealed record PublishAssistantCommand(
    long ExpectedDraftRevision,
    long? ExpectedPointerRevision);
