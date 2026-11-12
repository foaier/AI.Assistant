#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 知识文档从上传、处理到可用、失败或归档的状态。
/// </summary>
public enum AssistantKnowledgeDocumentStatus
{
    /// <summary>
    /// 文件已上传，尚待解析。
    /// </summary>
    Uploaded = 0,

    /// <summary>
    /// 任务正在处理。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 资源已就绪，可供运行使用。
    /// </summary>
    Ready = 2,

    /// <summary>
    /// 处理以失败状态结束。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 资源已归档，不再作为当前活动资源。
    /// </summary>
    Archived = 4
}
