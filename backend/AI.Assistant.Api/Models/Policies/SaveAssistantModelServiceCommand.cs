#nullable enable
namespace AI.Assistant.Models.Policies;

/// <summary>
/// 保存模型供应商设置并校验预期修订号的命令。
/// </summary>
public sealed record SaveAssistantModelServiceCommand(
    AssistantModelServiceSettings Settings,
    long ExpectedRevision);
