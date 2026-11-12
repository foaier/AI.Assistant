#nullable enable
namespace AI.Assistant.Models.Publication;

/// <summary>
/// 发布版本的只读快照、版本标识及发布和撤回审计信息。
/// </summary>
public sealed record AssistantPublishedVersionDto(
    string Id,
    string AssistantKey,
    string AssistantId,
    long VersionNo,
    string Name,
    string? Description,
    string SnapshotJson,
    string SnapshotHash,
    AssistantPublicationStatus Status,
    DateTime PublishedAt,
    DateTime? RevokedAt);
