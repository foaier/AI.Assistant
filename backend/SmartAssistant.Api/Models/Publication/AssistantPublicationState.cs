#nullable enable
namespace SmartAssistant.Models.Publication;

/// <summary>
/// 管理端显示的当前发布状态、公开标识和版本信息。
/// </summary>
public sealed record AssistantPublicationState(
    long PointerRevision,
    bool Published,
    string AssistantKey,
    long VersionNo,
    DateTime? PublishedAt);
