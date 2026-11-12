#nullable enable
namespace SmartAssistant.Models.Runtime;

/// <summary>
/// 幂等请求的处理结果：创建新操作、重放已有结果或检测到冲突。
/// </summary>
public enum AssistantIdempotencyOutcome
{
    /// <summary>
    /// 请求创建了新的业务操作。
    /// </summary>
    Created = 1,

    /// <summary>
    /// 请求复用了已保存的操作结果。
    /// </summary>
    Replayed = 2,

    /// <summary>
    /// 请求内容或并发状态与原操作冲突。
    /// </summary>
    Conflict = 3
}
