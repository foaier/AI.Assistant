#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 公开聊天创建匿名会话时提交的客户端信息。
/// </summary>
public sealed class CreatePublicSessionRequest
{
    /// <summary>
    /// 保存会话主体标识的摘要。
    /// </summary>
    public string? SubjectHash { get; set; }

    /// <summary>
    /// 记录会话或请求使用的语言区域。
    /// </summary>
    public string? Locale { get; set; }
}
