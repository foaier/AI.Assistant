#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 在公开助手的匿名会话中创建对话的命令。
/// </summary>
public sealed record CreateAssistantConversationCommand(
    string AssistantKey,
    string SessionId,
    string Locale);
