#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 提交用户消息的命令，包含对话标识、客户端幂等键和消息内容。
/// </summary>
public sealed record SubmitAssistantTurnCommand(
    string AssistantKey,
    string ConversationId,
    string ClientMessageId,
    string MessageType,
    string Text,
    string Locale);
