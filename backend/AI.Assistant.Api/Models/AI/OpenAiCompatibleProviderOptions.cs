#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 模型传输适配器的超时与响应体大小限制。
/// </summary>
public sealed record OpenAiCompatibleProviderOptions(
    TimeSpan Timeout,
    int MaxResponseBytes = 4 * 1024 * 1024);
