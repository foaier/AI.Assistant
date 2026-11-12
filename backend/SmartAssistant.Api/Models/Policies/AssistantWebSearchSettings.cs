#nullable enable
namespace SmartAssistant.Models.Policies;

/// <summary>
/// 联网搜索服务配置，包含服务接入与凭据设置。
/// </summary>
public sealed record AssistantWebSearchSettings(
    string Endpoint,
    string BotId,
    string ServiceName,
    string ApiKey,
    int TimeoutSeconds = 60);
