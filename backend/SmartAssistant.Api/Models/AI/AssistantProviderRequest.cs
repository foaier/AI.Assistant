#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 一次模型流式调用的请求，包含消息、模型参数和允许使用的工具。
/// </summary>
public sealed record AssistantProviderRequest(
    string ProviderProfileRef,
    string Model,
    IReadOnlyList<AssistantProviderMessage> Messages,
    double? Temperature = null,
    double? TopP = null,
    int? MaxTokens = null,
    IReadOnlyList<AssistantToolDefinition>? Tools = null,
    string? ToolChoice = null,
    bool? ParallelToolCalls = null,
    bool IncludeUsage = true,
    string? Instructions = null);
