#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 保存业务策略并校验预期修订号的命令。
/// </summary>
public sealed record SaveAssistantBusinessPolicyCommand(
    AssistantBusinessPolicySettings Settings,
    long ExpectedRevision);
