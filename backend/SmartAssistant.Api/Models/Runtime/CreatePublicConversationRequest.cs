#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 公开聊天创建对话时提交的语言设置。
/// </summary>
public sealed class CreatePublicConversationRequest
{
    /// <summary>
    /// 记录会话或请求使用的语言区域。
    /// </summary>
    public string? Locale { get; set; }
}
