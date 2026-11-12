#nullable enable
namespace SmartAssistant.Models.Knowledge;

/// <summary>
/// 知识索引任务的排队、执行和结束状态。
/// </summary>
public enum AssistantKnowledgeIndexJobStatus
{
    /// <summary>
    /// 任务等待处理。
    /// </summary>
    Queued = 0,

    /// <summary>
    /// 任务正在处理。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 表示成功这一业务状态、事件类型或处理结果。
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// 处理以失败状态结束。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 表示嵌入配置这一业务状态、事件类型或处理结果。
    /// </summary>
    NeedsEmbeddingConfig = 4
}
