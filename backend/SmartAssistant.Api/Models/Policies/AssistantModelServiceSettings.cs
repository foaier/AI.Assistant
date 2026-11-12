#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 模型供应商配置，包含服务地址、凭据引用和可用模型列表。
/// </summary>
public sealed record AssistantModelServiceSettings(
    string Name,
    string BaseUrl,
    string SecretReference,
    string SecretVersion,
    IReadOnlyList<AssistantModelOption> Models,
    int TimeoutSeconds = 90);
