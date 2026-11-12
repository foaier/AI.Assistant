#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 工具调用从请求、执行到成功、拒绝或失败的状态。
/// </summary>
public enum AssistantToolCallStatus
{
    /// <summary>
    /// 工具调用已提交，尚未开始执行。
    /// </summary>
    Requested = 1,

    /// <summary>
    /// 处理正在进行。
    /// </summary>
    Running = 2,

    /// <summary>
    /// 处理已成功完成。
    /// </summary>
    Completed = 3,

    /// <summary>
    /// 工具调用因权限或策略被拒绝。
    /// </summary>
    Denied = 4,

    /// <summary>
    /// 处理以失败状态结束。
    /// </summary>
    Failed = 5
}
