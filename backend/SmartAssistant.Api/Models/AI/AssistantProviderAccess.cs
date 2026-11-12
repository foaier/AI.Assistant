#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 解析供应商凭据所需的访问上下文，包含配置引用、模型和用途。
/// </summary>
public sealed record AssistantProviderAccess(
    string ProviderProfileRef,
    string Model,
    string Purpose = "chat",
    int? Dimension = null);
