#nullable enable
namespace AI.Assistant.Models.Knowledge;

/// <summary>
/// 修改知识库分块策略的 HTTP 请求体。
/// </summary>
public sealed class SaveChunkingPolicyRequest
{
    /// <summary>
    /// 保存请求中的模式字段。
    /// </summary>
    public AssistantKnowledgeChunkingMode Mode { get; set; } = AssistantKnowledgeChunkingMode.Hybrid;

    /// <summary>
    /// 限制单个文本片段的最大字符数。
    /// </summary>
    public int MaxChunkSize { get; set; } = 1200;

    /// <summary>
    /// 指定短片段合并所依据的最小字符数。
    /// </summary>
    public int MinChunkSize { get; set; } = 200;

    /// <summary>
    /// 指定相邻文本片段保留的重叠字符数。
    /// </summary>
    public int OverlapSize { get; set; } = 160;

    /// <summary>
    /// 限制用于文档分段的 Markdown 标题层级。
    /// </summary>
    public int MaxHeadingLevel { get; set; } = 3;

    /// <summary>
    /// 控制片段正文是否保留所属标题路径。
    /// </summary>
    public bool PreserveHeadingPath { get; set; } = true;

    /// <summary>
    /// 控制是否合并长度不足的相邻片段。
    /// </summary>
    public bool MergeShortChunks { get; set; } = true;

    /// <summary>
    /// 控制修改分块策略后是否重新处理已有文档。
    /// </summary>
    public bool ReprocessDocuments { get; set; }
}
