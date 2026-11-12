#nullable enable
namespace AI.Assistant.Models.AI;

/// <summary>
/// 模型供应商流式片段类型，用于区分文本、工具参数、结束、用量和错误。
/// </summary>
public enum AssistantProviderChunkKind
{
    /// <summary>
    /// 模型产生的文本增量。
    /// </summary>
    TextDelta = 1,

    /// <summary>
    /// 模型产生的工具调用参数增量。
    /// </summary>
    ToolCallDelta = 2,

    /// <summary>
    /// 处理已成功完成。
    /// </summary>
    Completed = 3,

    /// <summary>
    /// 模型报告的令牌用量。
    /// </summary>
    Usage = 4,

    /// <summary>
    /// 模型或下游服务返回错误。
    /// </summary>
    Error = 5
}
