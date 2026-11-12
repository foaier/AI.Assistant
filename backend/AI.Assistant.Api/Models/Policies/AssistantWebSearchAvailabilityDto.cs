#nullable enable
namespace AI.Assistant.Models.Policies;

/// <summary>
/// 供业务端判断是否可以启用联网搜索的可用性信息。
/// </summary>
public sealed record AssistantWebSearchAvailabilityDto(
    bool Configured,
    bool Enabled,
    long? Revision);
