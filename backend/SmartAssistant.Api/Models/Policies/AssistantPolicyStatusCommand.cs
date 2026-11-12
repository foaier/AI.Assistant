#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 按预期修订号切换模型供应商或策略的启用状态。
/// </summary>
public sealed record AssistantPolicyStatusCommand(
    bool Enabled,
    long ExpectedRevision);
