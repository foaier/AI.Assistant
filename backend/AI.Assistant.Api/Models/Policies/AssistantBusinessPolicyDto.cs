#nullable enable
namespace AI.Assistant.Models.Policies;

/// <summary>
/// 业务策略的管理视图，包含当前设置、修订和启用状态。
/// </summary>
public sealed record AssistantBusinessPolicyDto(
    string Id,
    long Revision,
    bool Enabled,
    AssistantBusinessPolicySettings Settings);
