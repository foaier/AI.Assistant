#nullable enable
namespace SmartAssistant.Models.Assistants;

/// <summary>
/// 助手草稿的原始配置与修订号，供应用层传递草稿状态。
/// </summary>
public sealed record AssistantDraftDto(
    string Id,
    string Name,
    string? Description,
    string DraftJson,
    long DraftRevision);
