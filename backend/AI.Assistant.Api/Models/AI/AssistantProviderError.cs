#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 模型适配器向编排器返回的标准化错误信息。
/// </summary>
public sealed record AssistantProviderError(
    string Code,
    string Message,
    bool Retryable,
    int? StatusCode = null,
    string? ProviderRequestId = null);
