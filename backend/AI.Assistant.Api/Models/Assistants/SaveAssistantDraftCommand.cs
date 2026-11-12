#nullable enable
namespace AI.Assistant.Models.Assistants;

/// <summary>
/// 保存助手草稿的命令，携带预期草稿修订号以避免覆盖并发修改。
/// </summary>
public sealed record SaveAssistantDraftCommand(
    string Name,
    string? Description,
    string DraftJson,
    long ExpectedDraftRevision);
