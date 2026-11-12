#nullable enable
namespace AI.Assistant.Models.Assistants;

/// <summary>
/// 助手列表所需的名称、描述、草稿修订号和最近修改信息。
/// </summary>
public sealed record AssistantListItemDto(
    string Id,
    string Name,
    string? Description,
    long DraftRevision);
