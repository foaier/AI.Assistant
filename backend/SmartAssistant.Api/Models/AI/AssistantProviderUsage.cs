#nullable enable
namespace SmartAssistant.Models.AI;

/// <summary>
/// 模型供应商报告的令牌用量。
/// </summary>
public sealed record AssistantProviderUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
