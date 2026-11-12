#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 公开聊天页面初始化数据，包含展示配置和可用能力。
/// </summary>
public sealed record AssistantPublicBootstrap(
    string AssistantKey,
    string AssistantId,
    string PublishedVersionId,
    string Locale,
    string? DisplayName,
    string? Description,
    string? WelcomeMessage,
    string? HomeIconUrl,
    IReadOnlySet<AssistantTransportKind> SupportedTransports,
    bool AnonymousChatEnabled,
    bool HistoryEnabled,
    AssistantExecutionMode ExecutionMode,
    string PrimaryColor = "#2563eb",
    IReadOnlyList<string>? SuggestedQuestions = null,
    IReadOnlyList<AssistantQuickLink>? QuickLinks = null,
    string? ChatBackgroundColor = null);
