#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 供模型适配器使用的服务地址与凭据材料。
/// </summary>
public sealed record OpenAiCompatibleProviderCredential(
    Uri BaseUrl,
    string ApiKey,
    string? Organization = null,
    TimeSpan? Timeout = null);
