#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 业务策略绑定的工具标识与契约版本。
/// </summary>
public sealed record AssistantToolBinding(
    string ToolId,
    string ContractVersion = "v1");
