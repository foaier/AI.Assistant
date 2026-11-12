#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 用户消息轮次从接收到完成、失败、取消或过期的生命周期状态。
/// </summary>
public enum AssistantTurnStatus
{
    /// <summary>
    /// 请求已受理，后续执行可能仍在进行。
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// 处理正在进行。
    /// </summary>
    Running = 2,

    /// <summary>
    /// 处理已成功完成。
    /// </summary>
    Completed = 3,

    /// <summary>
    /// 处理以失败状态结束。
    /// </summary>
    Failed = 4,

    /// <summary>
    /// 处理已取消。
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// 有效期已结束。
    /// </summary>
    Expired = 6
}
