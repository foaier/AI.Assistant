#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 匿名会话的有效、过期或撤销状态。
/// </summary>
public enum AssistantSessionStatus
{
    /// <summary>
    /// 当前处于可访问或生效状态。
    /// </summary>
    Active = 1,

    /// <summary>
    /// 有效期已结束。
    /// </summary>
    Expired = 2,

    /// <summary>
    /// 权限或发布状态已撤回。
    /// </summary>
    Revoked = 3
}
