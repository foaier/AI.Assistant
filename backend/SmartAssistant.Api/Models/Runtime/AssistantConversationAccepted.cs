#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 创建对话成功后返回的对话标识与相关状态。
/// </summary>
public sealed record AssistantConversationAccepted(
    string ConversationId,
    string SessionId,
    string PublishedVersionId,
    string Locale);
