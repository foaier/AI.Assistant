#nullable enable
namespace SmartAssistant.Models.Preview;

/// <summary>
/// 向草稿预览提交消息的请求，包含客户端幂等消息标识。
/// </summary>
public sealed record SubmitAssistantPreviewRequest(
    string ClientMessageId,
    string Text);
