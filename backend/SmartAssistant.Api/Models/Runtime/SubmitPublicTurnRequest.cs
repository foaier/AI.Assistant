#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 公开聊天提交消息的请求体，包含客户端幂等键、消息类型、文本和语言。
/// </summary>
public sealed class SubmitPublicTurnRequest
{
    /// <summary>
    /// 保留客户端幂等标识，用于识别同一消息的重试。
    /// </summary>
    public string ClientMessageId { get; set; } = string.Empty;

    /// <summary>
    /// 保存请求中的消息类型字段。
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// 保存请求中的文本字段。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 记录会话或请求使用的语言区域。
    /// </summary>
    public string? Locale { get; set; }
}
