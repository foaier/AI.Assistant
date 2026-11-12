#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 供系统管理端查看的联网搜索配置及修订状态。
/// </summary>
public sealed record AssistantWebSearchConfigDto(
    string Id,
    long Revision,
    bool Enabled,
    string Endpoint,
    string BotId,
    string ServiceName,
    bool CredentialConfigured,
    string CredentialMasked,
    int TimeoutSeconds);
