#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 对话的有效、归档或过期状态。
/// </summary>
public enum AssistantConversationStatus
{
    /// <summary>
    /// 当前处于可访问或生效状态。
    /// </summary>
    Active = 1,

    /// <summary>
    /// 资源已归档，不再作为当前活动资源。
    /// </summary>
    Archived = 2,

    /// <summary>
    /// 有效期已结束。
    /// </summary>
    Expired = 3
}
