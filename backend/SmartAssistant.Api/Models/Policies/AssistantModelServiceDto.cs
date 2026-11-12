#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 模型供应商配置的管理视图，包含修订和启用状态。
/// </summary>
public sealed record AssistantModelServiceDto(
    string Id,
    string Name,
    long Revision,
    bool Enabled,
    IReadOnlyList<AssistantModelOption> Models,
    AssistantModelServiceSettings? Settings);
