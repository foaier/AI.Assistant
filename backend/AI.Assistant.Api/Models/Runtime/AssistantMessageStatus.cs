#nullable enable
namespace AI.Assistant.Models.Runtime;

/// <summary>
/// 消息生成过程中的待处理、流式输出、完成或失败状态。
/// </summary>
public enum AssistantMessageStatus
{
    /// <summary>
    /// 结果尚未完成。
    /// </summary>
    Pending = 1,

    /// <summary>
    /// 正文正在持续输出。
    /// </summary>
    Streaming = 2,

    /// <summary>
    /// 处理已成功完成。
    /// </summary>
    Completed = 3,

    /// <summary>
    /// 处理以失败状态结束。
    /// </summary>
    Failed = 4
}
